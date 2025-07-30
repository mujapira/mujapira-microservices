using Microsoft.AspNetCore.HostFiltering;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddHealthChecks();

var jwtSection = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSection["Secret"]
    ?? throw new InvalidOperationException("JWT Secret is not configured.");

builder.Services
    .AddAuthentication("JwtBearer")
    .AddJwtBearer("JwtBearer", options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = "role"
        };

        options.MapInboundClaims = false;
    });

builder.Logging.ClearProviders();
builder.Logging.AddConsole()
       .SetMinimumLevel(LogLevel.Debug);

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

await app.UseOcelot();

app.Run();
