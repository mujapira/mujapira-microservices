using Contracts.Common; // Supondo que JwtSettings venha daqui
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://+:5000");

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var env = builder.Environment;

// =================================================================
// PASSO 1: REATIVANDO O CORS FUNCIONAL
// =================================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("DynamicCorsPolicy", policy =>
    {
        if (env.IsDevelopment())
        {
            // Política flexível para ambiente de desenvolvimento
            policy
                .SetIsOriginAllowed(_ => true) // Permite qualquer origem
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials(); // Permite credenciais (importante para testes locais)
        }
        else
        {
            // Política restrita e segura para produção
            policy
                .WithOrigins("https://mujapira.com", "https://www.mujapira.com") // Domínios exatos
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials(); // Permite credenciais (necessário para auth)
        }
    });
});


builder.Services.AddHealthChecks();

// Carregando configurações do JWT de forma segura
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException("JWT Secret não está configurado.");

if (jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret é muito curto; use um secreto forte e aleatório.");


// =================================================================
// PASSO 2: CORRIGINDO O REGISTRO DE AUTENTICAÇÃO
// Esta é a forma mais robusta e compatível com Ocelot.
// =================================================================
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = env.IsProduction();
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
        options.MapInboundClaims = false;
    });

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

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


app.MapHealthChecks("/health");

// =================================================================
// PASSO 3: HABILITANDO OS MIDDLEWARES NA ORDEM CORRETA
// =================================================================

// O CORS deve vir antes de qualquer coisa que precise dele, especialmente Ocelot.
app.UseCors("DynamicCorsPolicy");

// A autenticação deve vir antes do Ocelot para que as rotas possam ser protegidas.
app.UseAuthentication();
app.UseAuthorization();

// Ocelot é o último no pipeline para rotear a requisição.
await app.UseOcelot();

app.Run();
