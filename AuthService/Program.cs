using AuthService.Models;
using AuthService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration;

builder.Services.Configure<AuthJwtSettings>(configuration.GetSection("JwtSettings"));

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("Default")));

builder.Services.AddHostedService<MigrationService>();

builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
    client.BaseAddress = new Uri(configuration["Services:ApiGateway"]));

builder.Services.AddScoped<ITokenService, TokenService>();

var apiGatewayUrl = configuration["Services:ApiGateway"];

if (string.IsNullOrEmpty(apiGatewayUrl))
{
    throw new InvalidOperationException("ApiGateway URL is not configured.");
}

builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
    client.BaseAddress = new Uri(apiGatewayUrl));
builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();

var jwt = configuration.GetSection("JwtSettings").Get<AuthJwtSettings>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
        ValidateIssuerSigningKey = true
    };
});

builder.Services.AddControllers();
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();