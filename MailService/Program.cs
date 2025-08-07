using Confluent.Kafka;
using Contracts.Common;
using MailService.Services;
using MailService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

string host = Environment.GetEnvironmentVariable("SMTP_HOST")
    ?? throw new InvalidOperationException("SMTP_HOST não está configurado.");

string portEnv = Environment.GetEnvironmentVariable("SMTP_PORT")
    ?? throw new InvalidOperationException("SMTP_PORT não está configurado.");

if (!int.TryParse(portEnv, out var port) || port <= 0)
    throw new InvalidOperationException("SMTP_PORT inválido.");

string user = Environment.GetEnvironmentVariable("SMTP_USER")
    ?? throw new InvalidOperationException("SMTP_USER não está configurado.");

string appPwd = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD")
    ?? throw new InvalidOperationException("SMTP_APP_PASSWORD não está configurado.");

string from = Environment.GetEnvironmentVariable("SMTP_FROM")
    ?? throw new InvalidOperationException("SMTP_FROM não está configurado.");

// 2) Agora faz o bind seguro
builder.Services.Configure<SmtpSettings>(opts =>
{
    opts.Host = host;
    opts.Port = port;
    opts.User = user;
    opts.AppPassword = appPwd;
    opts.From = from;
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

string kafkaBootstrap = configuration.GetSection("Kafka")["BootstrapServers"] ?? "kafka:9092";

using var tempLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole().SetMinimumLevel(LogLevel.Debug));
var tempLogger = tempLoggerFactory.CreateLogger("KafkaReadiness");

await WaitForKafkaAsync(kafkaBootstrap, tempLogger);

// ===== Autenticação JWT =====
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

// Kafka consumer / log service
builder.Services.AddScoped<IMailService, MailService.Services.MailService>();
builder.Services.AddHostedService<MailConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHealthChecks();

builder.Services.AddOpenApi();

var app = builder.Build();

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