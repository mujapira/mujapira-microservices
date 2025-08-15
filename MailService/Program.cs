using System.Security.Claims;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MailService.Services;
using MailService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.IdentityModel.Tokens;

using Contracts.Common;
using Common.Observability;
using Common.Readiness.Kafka;

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
// SMTP (via env) + bind seguro
// =====================
string smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST")
    ?? throw new InvalidOperationException("SMTP_HOST não está configurado.");

string portEnv = Environment.GetEnvironmentVariable("SMTP_PORT")
    ?? throw new InvalidOperationException("SMTP_PORT não está configurado.");

if (!int.TryParse(portEnv, out var smtpPort) || smtpPort <= 0)
    throw new InvalidOperationException("SMTP_PORT inválido.");

string smtpUser = Environment.GetEnvironmentVariable("SMTP_USER")
    ?? throw new InvalidOperationException("SMTP_USER não está configurado.");

string smtpAppPwd = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD")
    ?? throw new InvalidOperationException("SMTP_APP_PASSWORD não está configurado.");

string smtpFrom = Environment.GetEnvironmentVariable("SMTP_FROM")
    ?? throw new InvalidOperationException("SMTP_FROM não está configurado.");

builder.Services.Configure<SmtpSettings>(opts =>
{
    opts.Host = smtpHost;
    opts.Port = smtpPort;
    opts.User = smtpUser;
    opts.AppPassword = smtpAppPwd;
    opts.From = smtpFrom;
});

// =====================
// Kafka & JWT
// =====================
builder.Services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
builder.Services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");

string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";


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
    var tmpLogger = tmpFactory.CreateLogger("StartupReadiness");
    using (tmpLogger.BeginScope(new Dictionary<string, object?>
    {
        ["Environment"] = env.EnvironmentName,
        ["KafkaBootstrap"] = Masking.MaskBootstrapServers(kafkaBootstrap),
        ["SmtpHost"] = smtpHost,
        ["SmtpUser"] = Masking.MaskKeepFirstLast(smtpUser, 2, 2)
    }))
    {
        tmpLogger.LogInformation("Iniciando readiness de Kafka e SMTP (bindings)...");
        await KafkaReadiness.WaitAsync(kafkaBootstrap, tmpLogger);
        tmpLogger.LogInformation("Dependências OK. Prosseguindo com DI e Build.");
    }
}

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

// =====================
// Serviços do domínio
// =====================
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddScoped<IMailService, MailService.Services.MailService>();
builder.Services.AddHostedService<MailConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApi();

var app = builder.Build();

// =====================
// Logger principal + banner seguro
// =====================
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
using (logger.BeginScope(new Dictionary<string, object?>
{
    ["Environment"] = env.EnvironmentName,
    ["KafkaBootstrap"] = Masking.MaskBootstrapServers(kafkaBootstrap),
    ["JwtSecretLen"] = jwtSettings.Secret.Length,
    ["SmtpHost"] = smtpHost,
    ["SmtpUser"] = Masking.MaskKeepFirstLast(smtpUser, 2, 2),
    ["SmtpFrom"] = Masking.Safe(smtpFrom)
}))
{
    logger.LogInformation("MailService iniciado com segurança e logging estruturado.");
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

app.UseRequestLogging();

app.MapHealthChecks("/health");

app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() => logger.LogInformation("ApplicationStarted"));
app.Lifetime.ApplicationStopping.Register(() => logger.LogInformation("ApplicationStopping"));
app.Lifetime.ApplicationStopped.Register(() => logger.LogInformation("ApplicationStopped"));

app.Run();
