using System.Text;
using LogService.Models;
using LogService.Services;
using LogService.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using System.Security.Claims;
using Contracts.Common;

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

// JWT
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

// MongoDB: registra o cliente e a coleção de LogEntry
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(opts.ConnectionString);
});
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    var db = client.GetDatabase(opts.DatabaseName);
    return db.GetCollection<LogEntry>(opts.CollectionName);
});

// Serviços
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
