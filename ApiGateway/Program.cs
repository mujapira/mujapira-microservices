using Contracts.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://+:5000");

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var env = builder.Environment;

//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("DynamicCors", policy =>
//    {
//        if (env.IsDevelopment())
//        {
//            policy
//                .SetIsOriginAllowed(_ => true)
//                .AllowAnyHeader()
//                .AllowAnyMethod()
//                .AllowCredentials();
//        }
//        else
//        {
//            policy
//                .WithOrigins("https://mujapira.com")
//                .AllowAnyHeader()
//                .AllowAnyMethod()
//                .AllowCredentials();
//        }
//    });
//});

builder.Services.AddHealthChecks();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");

if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret é muito curto; use um secreto forte e aleatório.");


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
        options.MapInboundClaims = false;
    });

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});
var logger = loggerFactory.CreateLogger("Startup");

if (env.IsDevelopment())
    logger.LogInformation("CORS policy: BBBBBBBBBB development mode, allowing all origins.");
else
    logger.LogInformation("CORS policy: AAAAAAAAAAAAAAA production mode, allowing only some origins");

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");

//app.UseCors("DynamicCors");

app.UseAuthentication();
app.UseAuthorization();

await app.UseOcelot();

app.Run();