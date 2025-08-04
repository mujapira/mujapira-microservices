using AuthService.Redis;
using AuthService.Services;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts.Auth;
using Contracts.Common;
using Contracts.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using System;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;
var env = builder.Environment;

// ===== Redis (rate limiting base infrastructure) =====
var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "redis";
var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

if (string.IsNullOrWhiteSpace(redisPassword))
    throw new InvalidOperationException("REDIS_PASSWORD (ou Redis:Password) não está configurado.");

var redisEndpoint = $"{redisHost}:{redisPort}";

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = new ConfigurationOptions
    {
        EndPoints = { redisEndpoint },
        Password = redisPassword,
        AbortOnConnectFail = false,
        ClientName = "auth-service",
        ConnectRetry = 3,
        ConnectTimeout = 5000
    };

    return ConnectionMultiplexer.Connect(options);
});

// ===== Settings binding & validation =====
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
builder.Services.Configure<HostFilteringOptions>(configuration.GetSection("HostFiltering"));
builder.Services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();

var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT secret não está configurado.");

if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT secret é muito curto; use um secreto forte.");

// ===== Dependency readiness (Postgres + Kafka) =====
// Criar logger temporário para essa fase
using var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("DependencyReadiness");

// Strings de conexão / bootstrap
string postgresConn = builder.Configuration.GetConnectionString("Auth")
    ?? throw new InvalidOperationException("Connection string 'Auth' não está configurada.");
string kafkaBootstrap = builder.Configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

// Espera o Postgres e Kafka antes de prosseguir
await WaitForPostgresAsync(postgresConn, tempLogger);
await WaitForKafkaAsync(kafkaBootstrap, tempLogger);

// ===== Infrastructure =====
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

builder.Services.AddDbContext<AuthDbContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("Auth")));

builder.Services.AddHostedService<MigrationService>();

var gatewayUrl = configuration["Services:ApiGateway"];
if (string.IsNullOrWhiteSpace(gatewayUrl))
    throw new InvalidOperationException("ApiGateway URL is not configured.");

builder.Services.AddHttpClient<IUserService, AuthService.Services.UserService>(client =>
    client.BaseAddress = new Uri(gatewayUrl));

// ===== Authentication / JWT =====
builder.Services
    .AddAuthentication("JwtBearer")
    .AddJwtBearer("JwtBearer", options =>
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

// ===== Auth / App =====
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

builder.Services.AddControllers();

// ===== Cookie policy =====
builder.Services.AddCookiePolicy(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.Secure = env.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

// ===== Logging =====
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var app = builder.Build();

// ===== logging info =====
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("AuthService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);
logger.LogInformation("Postgres connection: {Conn}", postgresConn);
logger.LogInformation("Kafka bootstrap servers: {Bootstrap}", kafkaBootstrap);

// ===== pipeline =====
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseHostFiltering();
app.UseCookiePolicy(); // precisa antes de auth if você depende
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
