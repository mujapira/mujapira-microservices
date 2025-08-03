using System;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UserService.Data;
using UserService.Middlewares;
using UserService.Services;
using Contracts.Common;
using Contracts.Users;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// configuração já carrega appsettings + appsettings.{Environment}.json + env vars
var configuration = builder.Configuration;
var env = builder.Environment;

// logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Host filtering
builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

//teste
// healthchecks

builder.Services.AddHealthChecks();

// Kafka
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// migração automática
builder.Services.AddHostedService<MigrationService>();

// DB
builder.Services.AddDbContext<CorpContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("User")));

// serviços de domínio
builder.Services.AddScoped<IUserService, UserService.Services.UserService>();

// JwtSettings binding e validação
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
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
        options.RequireHttpsMetadata = env.IsProduction(); // exige HTTPS metadata em produção
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

// autorização
builder.Services.AddAuthorization();

// controllers
builder.Services.AddControllers();

var app = builder.Build();

// middleware pipeline
app.UseHostFiltering(); 

app.UseMiddleware<GlobalExceptionLoggingMiddleware>();

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("UserService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);

app.Run();
