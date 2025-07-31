using System;
using System.Text;
using AuthService.Services;
using Contracts.Common;
using Contracts.Users;
using Contracts.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Host‐filtering
builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

// JWT settings
builder.Services.Configure<JwtSettings>(
    configuration.GetSection("JwtSettings")
);

// Kafka settings (para o IKafkaProducer)
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka")
);

// Registra o produtor de Kafka
builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

// Banco de dados
builder.Services.AddDbContext<AuthDbContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("Default"))
);

// Migration on startup
builder.Services.AddHostedService<MigrationService>();

// HTTP client para o UserService
var gatewayUrl = configuration["Services:ApiGateway"];
if (string.IsNullOrWhiteSpace(gatewayUrl))
    throw new InvalidOperationException("ApiGateway URL is not configured.");

builder.Services.AddHttpClient<IUserService, AuthService.Services.UserService>(client =>
    client.BaseAddress = new Uri(gatewayUrl)
);

// Serviços de token e auth
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

// Autenticação JWT
var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
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

builder.Services.AddControllers();

var app = builder.Build();

app.UseHostFiltering();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
