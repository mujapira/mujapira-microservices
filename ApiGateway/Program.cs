using System;
using System.Linq;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

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

var jwtSection = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured.");
var jwtIssuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured.");
var jwtAudience = jwtSection["Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured.");

builder.Services
    .AddAuthentication("JwtBearer")
    .AddJwtBearer("JwtBearer", options =>
    {
        options.RequireHttpsMetadata = env.IsProduction();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
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
    logger.LogInformation("CORS policy: development mode, allowing all origins.");
else
    logger.LogInformation("CORS policy: production mode, allowing only some origins");

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");

//app.UseCors("DynamicCors");

app.UseAuthentication();
app.UseAuthorization();

await app.UseOcelot();

app.Run();