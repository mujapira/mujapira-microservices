using System.Text;
using System.Security.Claims;
using Contracts.Common;
using LogService.Services;
using LogService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

using Common.Observability;
using Common.Readiness.Kafka;
using Common.Readiness.Mongo;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
    o.IncludeScopes = true;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

var configuration = builder.Configuration;
var env = builder.Environment;

// Host filtering + health
builder.Services.Configure<HostFilteringOptions>(configuration.GetSection("HostFiltering"));
builder.Services.AddHealthChecks();

// Mongo settings (bind + validação)
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

// Kafka & JWT
builder.Services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");
if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");

string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

// Variáveis para user root/app (usadas só na preparação)
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

static string Esc(string s) => Uri.EscapeDataString(s);
var rootConnString = $"mongodb://{Esc(rootUser)}:{Esc(rootPass)}@mongo:27017/admin?authSource=admin";
var limitedConnString = $"mongodb://{Esc(appUser)}:{Esc(appPassword)}@mongo:27017/{appDbName}?authSource={appDbName}";

// Readiness antes do Build
using (var tmpFactory = LoggerFactory.Create(lb =>
{
    lb.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
        o.IncludeScopes = true;
    });
    lb.SetMinimumLevel(LogLevel.Information);
}))
{
    var tmpLog = tmpFactory.CreateLogger("StartupReadiness");
    using (tmpLog.BeginScope(new Dictionary<string, object?>
    {
        ["Environment"] = env.EnvironmentName,
        ["KafkaBootstrap"] = Masking.MaskBootstrapServers(kafkaBootstrap),
        ["MongoRoot"] = Masking.MaskMongoConnectionString(rootConnString),
        ["MongoApp"] = Masking.MaskMongoConnectionString(limitedConnString),
        ["AppDb"] = appDbName,
        ["AppUser"] = Masking.MaskKeepFirstLast(appUser, 2, 2)
    }))
    {
        tmpLog.LogInformation("Iniciando readiness de Mongo e Kafka...");
        await MongoReadiness.WaitAsync(rootConnString, tmpLog);
        await KafkaReadiness.WaitAsync(kafkaBootstrap, tmpLog);
        await MongoReadiness.EnsureUserAsync(rootConnString, appDbName, appUser, appPassword, tmpLog);
        tmpLog.LogInformation("Dependências OK. Prosseguindo com DI e Build.");
    }
}

// Mongo client com usuário limitado
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(limitedConnString));
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    var db = client.GetDatabase(opts.DatabaseName);
    return db.GetCollection<LogService.Models.LogEntry>(opts.CollectionName);
});

// AuthN / AuthZ
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
builder.Services.AddAuthorization();

// Domínio
builder.Services.AddScoped<ILogService, LogService.Services.LogService>();
builder.Services.AddHostedService<LogConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Banner seguro
var banner = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
using (banner.BeginScope(new Dictionary<string, object?>
{
    ["Environment"] = env.EnvironmentName,
    ["KafkaBootstrap"] = Masking.MaskBootstrapServers(kafkaBootstrap),
    ["JwtSecretLen"] = jwtSettings.Secret.Length,
    ["MongoApp"] = Masking.MaskMongoConnectionString(limitedConnString),
    ["AppDb"] = appDbName,
    ["AppUser"] = Masking.MaskKeepFirstLast(appUser, 2, 2)
}))
{
    banner.LogInformation("LogService iniciado com segurança e logging estruturado.");
}

// Pipeline
app.UseHostFiltering();
if (env.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Access log + correlação
app.UseRequestLogging();

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ciclo de vida
app.Lifetime.ApplicationStarted.Register(() => banner.LogInformation("ApplicationStarted"));
app.Lifetime.ApplicationStopping.Register(() => banner.LogInformation("ApplicationStopping"));
app.Lifetime.ApplicationStopped.Register(() => banner.LogInformation("ApplicationStopped"));

app.Run();
