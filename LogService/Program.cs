using System;
using System.Text;
using System.Security.Claims;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts.Common;
using LogService.Models;
using LogService.Services;
using LogService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Bson;

var builder = WebApplication.CreateBuilder(args);

// configuração (appsettings.json, appsettings.{Environment}.json e env vars já são carregados por padrão)
var configuration = builder.Configuration;
var env = builder.Environment;

// logging precoce para readiness
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Host filtering
builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

// Health checks
builder.Services.AddHealthChecks();

// ===== MongoDB =====
// Bind e validação das settings
var mongoSettingsSection = configuration.GetSection("MongoDb");
var mongoSettings = mongoSettingsSection.Get<MongoDbSettings>()
    ?? throw new InvalidOperationException("MongoDbSettings ausente.");

if (string.IsNullOrWhiteSpace(mongoSettings.ConnectionString))
    throw new InvalidOperationException("MongoDB connection string não configurada.");
if (string.IsNullOrWhiteSpace(mongoSettings.DatabaseName))
    throw new InvalidOperationException("MongoDB database name não configurado.");
if (string.IsNullOrWhiteSpace(mongoSettings.CollectionName))
    throw new InvalidOperationException("MongoDB collection name não configurado.");

builder.Services.Configure<MongoDbSettings>(mongoSettingsSection);

// ===== Kafka & JWT =====
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka"));
builder.Services.Configure<JwtSettings>(
    configuration.GetSection("JwtSettings"));

// Bind e validação de JWT settings
var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");

// ===== Dependency readiness (Mongo + Kafka + user creation) =====
// logger temporário para fase de readiness
using var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("DependencyReadiness");

// Strings de conexão
string mongoConn = mongoSettings.ConnectionString;
string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

// Espera Mongo e Kafka básicos
await WaitForMongoAsync(mongoConn, tempLogger);
await WaitForKafkaAsync(kafkaBootstrap, tempLogger);

// Garante usuário limitado no Mongo usando root
var rootUser = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME")
    ?? throw new InvalidOperationException("MONGO_INITDB_ROOT_USERNAME ausente.");
var rootPass = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD")
    ?? throw new InvalidOperationException("MONGO_INITDB_ROOT_PASSWORD ausente.");

var appDbName = Environment.GetEnvironmentVariable("LOG_DB_NAME")
    ?? throw new InvalidOperationException("LOG_DB_NAME ausente.");
var appUser = Environment.GetEnvironmentVariable("LOG_DB_USER")
    ?? throw new InvalidOperationException("LOG_DB_USER ausente.");
var appPassword = Environment.GetEnvironmentVariable("LOG_DB_PASSWORD")
    ?? throw new InvalidOperationException("LOG_DB_PASSWORD ausente.");

// Conexão de root para manipulação
var rootConnString = $"mongodb://{rootUser}:{rootPass}@mongo:27017/admin?authSource=admin&retryWrites=true&w=majority";
await EnsureMongoUserAndDbAsync(rootConnString, appDbName, appUser, appPassword, tempLogger);

// Agora registra client Mongo com usuário limitado
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    // monta connection string do app user
    var limitedConn = $"mongodb://{appUser}:{appPassword}@mongo:27017/{appDbName}?authSource={appDbName}&retryWrites=true&w=majority";
    return new MongoClient(limitedConn);
});

// Registro da coleção tipada usando o usuário limitado
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    var db = client.GetDatabase(opts.DatabaseName);
    return db.GetCollection<LogEntry>(opts.CollectionName);
});

// ===== Autenticação JWT =====
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = env.IsProduction();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = ClaimTypes.Role
        };
    });

// Kafka consumer / log service
builder.Services.AddScoped<ILogService, LogService.Services.LogService>();
builder.Services.AddHostedService<KafkaLogConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// log inicial para debug
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("LogService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);
logger.LogInformation("Mongo connection string (root): {Conn}", mongoConn);
logger.LogInformation("Kafka bootstrap servers: {Bootstrap}", kafkaBootstrap);

// pipeline
app.UseHostFiltering();

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();


// ---- helpers ----

static async Task WaitForKafkaAsync(string bootstrapServers, ILogger logger, int maxRetries = 8)
{
    var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
    int attempt = 0;
    TimeSpan delay = TimeSpan.FromSeconds(1);
    while (true)
    {
        try
        {
            using var admin = new AdminClientBuilder(config).Build();
            var meta = admin.GetMetadata(TimeSpan.FromSeconds(2));
            logger.LogInformation("Kafka disponível, tópicos: {Count}", meta.Topics.Count);
            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Não foi possível conectar ao Kafka após {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Kafka não disponível (tentativa {Attempt}), retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2;
        }
    }
}

static async Task WaitForMongoAsync(string connectionString, ILogger logger, int maxRetries = 8)
{
    int attempt = 0;
    TimeSpan delay = TimeSpan.FromSeconds(1);
    while (true)
    {
        try
        {
            var client = new MongoClient(connectionString);
            var db = client.GetDatabase("admin");
            await db.RunCommandAsync((Command<dynamic>)"{ ping: 1 }");
            logger.LogInformation("Mongo disponível.");
            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Não foi possível conectar ao Mongo após {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Mongo não disponível (tentativa {Attempt}), retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2;
        }
    }
}

static async Task EnsureMongoUserAndDbAsync(
    string rootConnectionString,
    string appDbName,
    string appUser,
    string appPassword,
    ILogger logger,
    int maxRetries = 5)
{
    int attempt = 0;
    TimeSpan delay = TimeSpan.FromSeconds(1);

    while (true)
    {
        try
        {
            var rootClient = new MongoClient(rootConnectionString);
            var appDb = rootClient.GetDatabase(appDbName);

            // Verifica se o usuário já existe
            var usersInfo = await appDb.RunCommandAsync<BsonDocument>(new BsonDocument { { "usersInfo", appUser } });
            var userArray = usersInfo.GetValue("users").AsBsonArray;

            if (userArray.Count == 0)
            {
                logger.LogInformation("Usuário Mongo '{User}' não existe em '{Db}', criando com role readWrite.", appUser, appDbName);
                var createUserCmd = new BsonDocument
                {
                    { "createUser", appUser },
                    { "pwd", appPassword },
                    {
                        "roles", new BsonArray
                        {
                            new BsonDocument { { "role", "readWrite" }, { "db", appDbName } }
                        }
                    }
                };
                await appDb.RunCommandAsync<BsonDocument>(createUserCmd);
                logger.LogInformation("Usuário Mongo criado com sucesso.");
            }
            else
            {
                logger.LogInformation("Usuário Mongo '{User}' já existe em '{Db}'.", appUser, appDbName);
            }

            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Falha garantindo usuário Mongo após {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Tentativa {Attempt} para garantir usuário Mongo falhou, retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2;
        }
    }
}
