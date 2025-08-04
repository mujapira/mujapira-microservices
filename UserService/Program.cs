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
using Confluent.Kafka;
using Confluent.Kafka.Admin;

var builder = WebApplication.CreateBuilder(args);

// configura��o j� carrega appsettings + appsettings.{Environment}.json + env vars
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

// healthchecks
builder.Services.AddHealthChecks();

// Kafka settings binding
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka"));

// --- espera Kafka estar dispon�vel antes de prosseguir ---
// Esse bloco roda antes de registrar produtores/consumidores efetivos
var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("KafkaReadiness");

string bootstrapServers = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";
await WaitForKafkaAsync(bootstrapServers, tempLogger);

// registro do produtor/servi�o Kafka
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// migra��o autom�tica
builder.Services.AddHostedService<MigrationService>();

// DB
builder.Services.AddDbContext<CorpContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("User")));

// servi�os de dom�nio
builder.Services.AddScoped<IUserService, UserService.Services.UserService>();

// JwtSettings binding e valida��o
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Se��o JwtSettings est� ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret n�o est� configurado.");

if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret � muito curto; use um secreto forte e aleat�rio.");

// Autentica��o JWT
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = env.IsProduction(); // exige HTTPS metadata em produ��o
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

// autoriza��o
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
logger.LogInformation("Kafka bootstrap servers: {Bootstrap}", bootstrapServers);

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
            logger.LogInformation("Kafka dispon�vel, t�picos: {Count}", meta.Topics.Count);
            return;
        }
        catch (Exception ex)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                logger.LogError(ex, "N�o foi poss�vel conectar ao Kafka ap�s {Attempt} tentativas.", attempt);
                throw;
            }
            logger.LogWarning("Kafka n�o dispon�vel (tentativa {Attempt}), retry em {Delay}s: {Msg}", attempt, delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
            delay = delay * 2; // backoff exponencial
        }
    }
}
