using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.EntityFrameworkCore;
using System.Text;
using UserService.Data;
using UserService.Middlewares;
using UserService.Services;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Contracts.Common;
using Contracts.Users;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.Configure<HostFilteringOptions>(
    configuration.GetSection("HostFiltering")
);

builder.Services.AddHealthChecks();

builder.Services.Configure<KafkaSettings>(
    configuration.GetSection("Kafka")
);

builder.Services.AddHostedService<MigrationService>();

builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();

builder.Services.AddDbContext<CorpContext>(opts =>
    opts.UseNpgsql(configuration.GetConnectionString("Default"))
);

builder.Services.AddScoped<IUserService, UserService.Services.UserService>();

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
builder.Services.AddAuthorization();

builder.Services.AddControllers();

var app = builder.Build();

app.UseHostFiltering();                     // 1) bloqueia requisições fora do Gateway
app.UseMiddleware<GlobalExceptionLoggingMiddleware>();

app.MapHealthChecks("/health");             // 2) health?check

app.UseAuthentication();                    // 3) autenticação JWT
app.UseAuthorization();                     // 4) autorização via [Authorize]

app.MapControllers();                       // 5) endpoints

app.Run();
