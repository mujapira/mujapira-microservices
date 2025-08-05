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
using System.Threading.Tasks;

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

// ===== logging precoce para readiness =====
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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

// ===== Dependency readiness (Postgres + Kafka + Redis) =====
// logger temporário para essa fase
using var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("DependencyReadiness");

// Strings de conexão / bootstrap
string postgresConn = configuration.GetConnectionString("Auth")
    ?? throw new InvalidOperationException("Connection string 'Auth' não está configurada.");
string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

// espera os três
await WaitForPostgresAsync(postgresConn, tempLogger);
await WaitForKafkaAsync(kafkaBootstrap, tempLogger);
await WaitForRedisAsync(redisEndpoint, redisPassword, tempLogger);

// ===== Infraestrutura após dependências =====
// Redis multiplexer (rate limiter)
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

// Kafka producer
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// Postgres / EF
builder.Services.AddDbContext<AuthDbContext>(opts =>
    opts.UseNpgsql(postgresConn));

// migração automática
builder.Services.AddHostedService<MigrationService>();

// API gateway client
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

// ===== App =====
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();
builder.Services.AddControllers();
builder.Services.AddHealthChecks(); // health endpoint

// ===== Cookie policy =====
builder.Services.AddCookiePolicy(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.Secure = env.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// ===== logging info =====
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("AuthService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);
logger.LogInformation("Postgres connection: {Conn}", postgresConn);
logger.LogInformation("Kafka bootstrap servers: {Bootstrap}", kafkaBootstrap);
logger.LogInformation("Redis endpoint: {Redis}", redisEndpoint);

// ===== pipeline =====
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseHostFiltering();
app.UseCookiePolicy();

app.Use(async (ctx, next) =>
{
    var hasAuth = ctx.Request.Headers.TryGetValue("Authorization", out var val);
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogDebug(">> UserService received Authorization header? {Has} – {Val}", hasAuth, val);
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

// readiness simples combinando dependências (opcional, pode expandir)
app.MapGet("/ready", () => Results.Ok(new { status = "ready" })).WithName("Readiness");

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

static async Task WaitForRedisAsync(string endpoint, string password, ILogger logger, int maxRetries = 8)
{
    int attempt = 0;
    TimeSpan delay = TimeSpan.FromSeconds(1);
    var config = new ConfigurationOptions
    {
        EndPoints = { endpoint },
        Password = password,
        AbortOnConnectFail = false,
        ConnectTimeout = 2000
    };

    while (true)
    {
        try
        {
            using var mux = await ConnectionMultiplexer.ConnectAsync(config);
            var db = mux.GetDatabase();
            var pong = await db.PingAsync();
            logger.LogInformation("Redis disponível (ping {Ping}ms).", pong.TotalMilliseconds);
            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "Não foi possível conectar ao Redis após {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Redis não disponível (tentativa {Attempt}), retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2;
        }
    }
}
