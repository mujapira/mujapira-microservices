using LogService.Configurations;
using LogService.Services;
using LogService.Settings;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// 1) Configurações do MongoDB e Kafka
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection("Kafka"));

// 2) Serviços e o consumer Kafka como HostedService
builder.Services.AddScoped<ILogService, LogService.Services.LogService>();
builder.Services.AddHostedService<KafkaLogConsumer>();

// 3) MVC + Swagger/OpenAPI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LogService API",
        Version = "v1",
        Description = "API para leitura/escrita de logs via Kafka + MongoDB"
    });
});

var app = builder.Build();

app.MapHealthChecks("/health");

// 4) Middleware de Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LogService API v1");
    c.RoutePrefix = string.Empty;  // Swagger UI disponível em http://localhost:5000/
});

// 5) (Opcional) Health‑check simples
app.MapGet("/health", () => Results.Ok("up"));

app.UseAuthorization();
app.MapControllers();

app.Run();
