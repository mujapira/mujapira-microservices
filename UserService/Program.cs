using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts.Common;
using Contracts.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System;
using System.Security.Claims;
using System.Text;
using UserService.Data;
using UserService.Middlewares;
using UserService.Services;

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

// ===== Kafka & JWT =====
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka"));
builder.Services.Configure<JwtSettings>(
    configuration.GetSection("JwtSettings"));

// JwtSettings binding e validação
var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");
if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");
if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret é muito curto; use um secreto forte e aleatório.");

// Autenticação JWT
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
            ClockSkew = TimeSpan.FromMinutes(5),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = ClaimTypes.Role
        };
    });

// autorização e controllers
builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ===== readiness dependencies =====
// logger temporário
using var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("KafkaReadiness");

// Strings de bootstrap / conexão
string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";
string userConnString = configuration.GetConnectionString("User")
    ?? throw new InvalidOperationException("Connection string 'User' não está configurada.");

// Espera Kafka e Postgres
await WaitForKafkaAsync(kafkaBootstrap, tempLogger);
await WaitForPostgresAsync(userConnString, tempLogger);

// ===== infra e serviços depois que dependências estão ok =====
// DbContext (Postgres)
builder.Services.AddDbContext<CorpContext>(opts =>
    opts.UseNpgsql(userConnString));

// Kafka producer (ou consumer, conforme necessário)
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// migração automática
builder.Services.AddHostedService<MigrationService>();

// serviços de domínio
builder.Services.AddScoped<IUserService, UserService.Services.UserService>();

// binding de JwtSettings (já feito acima, mas reafirmando para uso)
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

// build app
var app = builder.Build();

// logging inicial
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("UserService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);
logger.LogInformation("Kafka bootstrap servers: {Bootstrap}", kafkaBootstrap);
logger.LogInformation("Postgres connection string (User): {Conn}", userConnString);

// pipeline
app.UseHostFiltering();
app.UseMiddleware<GlobalExceptionLoggingMiddleware>();

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

static async Task WaitForPostgresAsync(string connectionString, ILogger logger, int maxRetries = 8)
{
    int attempt = 0;
    TimeSpan delay = TimeSpan.FromSeconds(1);
    while (true)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            // simples ping
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            logger.LogInformation("Postgres disponível.");
            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Não foi possível conectar ao Postgres após {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Postgres não disponível (tentativa {Attempt}), retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2;
        }
    }
}
