using AuthService.Services;
using Contracts.Auth;
using Contracts.Common;
using Contracts.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AuthService.Redis;

var builder = WebApplication.CreateBuilder(args);

// atalho
var configuration = builder.Configuration;
var env = builder.Environment;

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration;
    var redisHost = configuration["REDIS_HOST"] ?? "redis:6379";
    var redisPassword = configuration["REDIS_PASSWORD"];
    var options = ConfigurationOptions.Parse(redisHost);
    if (!string.IsNullOrWhiteSpace(redisPassword))
        options.Password = redisPassword;
    options.ClientName = "auth-service";
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
        options.MapInboundClaims = false;
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
