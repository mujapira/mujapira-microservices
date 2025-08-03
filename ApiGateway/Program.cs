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

// health
builder.Services.AddHealthChecks();

// JWT settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");
if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret é muito curto; use um secreto forte e aleatório.");

// Autenticação JWT (mapeamento padrão ativo)
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = env.IsProduction();
        // NÃO desative o MapInboundClaims: deixamos o padrão para que "role" vire ClaimTypes.Role
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
            // RoleClaimType padrão já é ClaimTypes.Role, pode omitir, mas deixamos para clareza
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

// pipeline
app.UseCors("DynamicCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

// Ocelot por último
await app.UseOcelot();

app.Run();
