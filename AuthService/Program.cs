using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

builder.Services.Configure<AuthJwtSettings>(
    configuration.GetSection("JwtSettings")
);

builder.Services.AddDbContext<AuthDbContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("Default"))
);

builder.Services.AddHostedService<MigrationService>();

var gatewayUrl = configuration["Services:ApiGateway"];
if (string.IsNullOrWhiteSpace(gatewayUrl))
    throw new InvalidOperationException("ApiGateway URL is not configured.");
builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
    client.BaseAddress = new Uri(gatewayUrl)
);

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

var jwtSettings = configuration.GetSection("JwtSettings").Get<AuthJwtSettings>();
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
