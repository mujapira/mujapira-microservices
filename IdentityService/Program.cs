using System.Security.Claims;
using System.Text;
using Contracts.Common;
using Contracts.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using Common.Observability;
using Common.Readiness.Postgres;
using Common.Readiness.Kafka;
using Common.Readiness.Redis;

using IdentityService.Data;
using IdentityService.Services;


using StackExchange.Redis;

using IdentityService.Redis;

var builder = WebApplication.CreateBuilder(args);

// =====================
// Logging inicial
// =====================
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
    o.IncludeScopes = true;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

// =====================
// Config & env
// =====================
var configuration = builder.Configuration;
var env = builder.Environment;

// =====================
// Host filtering & Health
// =====================
builder.Services.Configure<HostFilteringOptions>(configuration.GetSection("HostFiltering"));
builder.Services.AddHealthChecks();

// =====================
// Settings (Kafka/JWT) + validações
// =====================
builder.Services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

var jwt = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings ausente.");
if (string.IsNullOrWhiteSpace(jwt.Secret)) throw new InvalidOperationException("JWT Secret não configurado.");
if (jwt.Secret.Length < 16) throw new InvalidOperationException("JWT Secret muito curto.");
if (jwt.AccessTokenExpirationMinutes <= 0) throw new InvalidOperationException("AccessTokenExpirationMinutes inválido.");
if (jwt.RefreshTokenExpirationDays <= 0) throw new InvalidOperationException("RefreshTokenExpirationDays inválido.");

var pgConn = configuration.GetConnectionString("Identity")
    ?? throw new InvalidOperationException("Connection string 'Identity' não configurada.");
var kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "redis";
var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD"); // se null, Redis fica desativado
var redisEnabled = !string.IsNullOrWhiteSpace(redisPassword);
var redisEndpoint = $"{redisHost}:{redisPort}";

// =====================
// Readiness ANTES do Build
// =====================
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
    var log = tmpFactory.CreateLogger("StartupReadiness");
    using (log.BeginScope(new Dictionary<string, object?>
    {
        ["Environment"] = env.EnvironmentName,
        ["Postgres"] = Masking.MaskPostgresConnectionString(pgConn),
        ["Kafka"] = Masking.MaskBootstrapServers(kafkaBootstrap),
        ["Redis"] = redisEnabled ? Masking.MaskRedisEndpoint(redisEndpoint) : "(disabled)"
    }))
    {
        log.LogInformation("Iniciando readiness (Postgres/Kafka{RedisFlag})...",
            redisEnabled ? "/Redis" : string.Empty);

        await PostgresReadiness.WaitAsync(pgConn, log);
        await KafkaReadiness.WaitAsync(kafkaBootstrap, log);
        if (redisEnabled)
            await RedisReadiness.WaitAsync(redisEndpoint, redisPassword!, log);

        log.LogInformation("Dependências OK. Prosseguindo com DI e Build.");
    }
}

// =====================
// DI pós-readiness
// =====================

// EF/DbContext unificado
builder.Services.AddDbContext<CorpContext>(opts => opts.UseNpgsql(pgConn));

// Kafka producer
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// Token + Domain service unificado (Auth + Users)
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Redis multiplexer (opcional)
if (redisEnabled)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = new ConfigurationOptions
        {
            EndPoints = { redisEndpoint },
            Password = redisPassword,
            AbortOnConnectFail = false,
            ClientName = "identity-service",
            ConnectRetry = 3,
            ConnectTimeout = 5000
        };
        return ConnectionMultiplexer.Connect(options);
    });

    builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
}

// Migração automática
builder.Services.AddHostedService<MigrationService>();

// =====================
// AuthN / AuthZ
// =====================
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = env.IsProduction();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddOpenApi(); // opcional no .NET 9

var app = builder.Build();

// =====================
// Banner seguro
// =====================
var banner = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
using (banner.BeginScope(new Dictionary<string, object?>
{
    ["Environment"] = env.EnvironmentName,
    ["Postgres"] = Masking.MaskPostgresConnectionString(pgConn),
    ["Kafka"] = Masking.MaskBootstrapServers(kafkaBootstrap),
    ["Redis"] = redisEnabled ? Masking.MaskRedisEndpoint(redisEndpoint) : "(disabled)",
    ["JwtSecretLen"] = jwt.Secret.Length,
    ["JwtIssuer"] = Masking.Safe(jwt.Issuer),
    ["JwtAudience"] = Masking.Safe(jwt.Audience)
}))
{
    banner.LogInformation("IdentityService iniciado com segurança e logging estruturado.");
}

// =====================
// Pipeline
// =====================
app.UseHostFiltering();

if (env.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Access log + correlação
app.UseRequestLogging();

// Health
app.MapHealthChecks("/health");
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ciclo de vida (útil em orquestradores)
app.Lifetime.ApplicationStarted.Register(() => banner.LogInformation("ApplicationStarted"));
app.Lifetime.ApplicationStopping.Register(() => banner.LogInformation("ApplicationStopping"));
app.Lifetime.ApplicationStopped.Register(() => banner.LogInformation("ApplicationStopped"));

app.Run();
