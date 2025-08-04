using Contracts.Common; // JwtSettings
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// expõe em 5000
builder.WebHost.UseUrls("http://+:5000");

// configuração
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var env = builder.Environment;

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DynamicCorsPolicy", policy =>
    {
        if (env.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy
                .WithOrigins("https://mujapira.com", "https://www.mujapira.com")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// health & http client
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

// JWT settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");
if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret é muito curto; use um secreto forte e aleatório.");

// Autenticação JWT
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
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
    });

// logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Ocelot
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

// forwarded headers (Nginx / Cloudflare)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (env.IsDevelopment())
    logger.LogInformation("CORS policy: development mode, allowing all origins.");
else
    logger.LogInformation("CORS policy: production mode, restricting origins.");

// endpoints básicos
app.MapHealthChecks("/health");

app.MapGet("/ready", async (IHttpClientFactory httpFactory, ILogger<Program> logger) =>
{
    var downstreams = new[]
    {
        new { Name = "authservice", Url = "http://localhost:5003/health" },
        new { Name = "userservice", Url = "http://localhost:5002/health" },
        new { Name = "logservice", Url = "http://localhost:5001/health" }
    };

    var overallHealthy = true;
    var detail = new Dictionary<string, object>();

    foreach (var svc in downstreams)
    {
        using var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            var resp = await client.GetAsync(svc.Url);
            if (resp.IsSuccessStatusCode)
            {
                detail[svc.Name] = new
                {
                    status = "Healthy",
                    httpStatus = (int)resp.StatusCode
                };
            }
            else
            {
                overallHealthy = false;
                detail[svc.Name] = new
                {
                    status = "Unhealthy",
                    httpStatus = (int)resp.StatusCode,
                    reason = $"Status code {resp.StatusCode}"
                };
            }
        }
        catch (Exception ex)
        {
            overallHealthy = false;
            detail[svc.Name] = new
            {
                status = "Unavailable",
                error = ex.Message
            };
        }
    }

    var result = new
    {
        status = overallHealthy ? "Healthy" : "Degraded",
        services = detail
    };

    return overallHealthy
        ? Results.Ok(result)
        : Results.StatusCode(503);
})
.WithName("Readiness");

// pipeline condicional: só aplica Ocelot/CORS/Auth para rotas que não sejam /health ou /ready
app.UseWhen(context =>
    !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) &&
    !context.Request.Path.StartsWithSegments("/ready", StringComparison.OrdinalIgnoreCase),
    appBuilder =>
    {
        appBuilder.UseCors("DynamicCorsPolicy");
        appBuilder.UseAuthentication();
        appBuilder.UseAuthorization();
        appBuilder.UseOcelot().Wait();
    });

app.Run();
