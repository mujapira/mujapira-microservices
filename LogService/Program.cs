using System;
using System.Text;
using System.Security.Claims;
using LogService.Models;
using LogService.Services;
using LogService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Contracts.Common;

var builder = WebApplication.CreateBuilder(args);

// configuração (appsettings.json, appsettings.{Environment}.json e env vars já são carregados por padrão)
var configuration = builder.Configuration;
var env = builder.Environment;

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

// Registro do cliente Mongo (singleton)
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(opts.ConnectionString);
});

// Registro da coleção tipada
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    var db = client.GetDatabase(opts.DatabaseName);
    return db.GetCollection<LogEntry>(opts.CollectionName);
});

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
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = ClaimTypes.Role
        };
        options.MapInboundClaims = false;
    });

// Kafka consumer / log service
builder.Services.AddScoped<ILogService, LogService.Services.LogService>();
builder.Services.AddHostedService<KafkaLogConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// logging info inicial
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var app = builder.Build();

// log inicial para debug
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("LogService iniciado em ambiente {Env}", env.EnvironmentName);
logger.LogInformation("JWT Issuer: {Issuer}, Audience: {Audience}", jwtSettings.Issuer, jwtSettings.Audience);

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
