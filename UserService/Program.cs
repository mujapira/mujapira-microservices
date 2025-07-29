using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using UserService.Data;
using UserService.Services;
using UserService.Settings;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("Default");

builder.Services.AddHealthChecks();

builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection("Kafka"));

builder.Services.AddHostedService<MigrationService>();

builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
builder.Services.AddDbContext<CorpContext>(opts =>
    opts.UseNpgsql(cs));

builder.Services.AddScoped<IUserService, UserService.Services.UserService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
  c.SwaggerDoc("v1", new OpenApiInfo { Title = "UserService", Version = "v1" }));

var app = builder.Build();

app.UseMiddleware<UserService.Middlewares.GlobalExceptionLoggingMiddleware>();

app.MapHealthChecks("/health");

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
