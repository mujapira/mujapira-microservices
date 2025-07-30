using System.Text;
using LogService.Configurations;
using LogService.Services;
using LogService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

builder.Services.AddHealthChecks();

builder.Services.Configure<MongoDbSettings>(
    configuration.GetSection("MongoDb")
);
builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka")
);

builder.Services.Configure<JwtSettings>(
    configuration.GetSection("JwtSettings")
);

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

builder.Services.AddScoped<ILogService, LogService.Services.LogService>();
builder.Services.AddHostedService<KafkaLogConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseHostFiltering();

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
