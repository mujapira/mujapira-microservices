using Contracts.Common; // JwtSettings
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var env = builder.Environment;

// 1) URLs & Configuration
builder.WebHost.UseUrls("http://+:5000");
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// 2) Core services
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DynamicCorsPolicy", policy =>
    {
        if (env.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy
                .WithOrigins("https://mujapira.com", "https://www.mujapira.com")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

// 3) JWT authentication & authorization
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção JwtSettings está ausente.");
if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 16)
    throw new InvalidOperationException("JWT Secret não está configurado ou é muito curto.");

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
            ClockSkew = TimeSpan.FromMinutes(5),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuerSigningKey = true,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

// 4) Ocelot + token forwarding
builder.Services
    .AddOcelot(builder.Configuration)
    .AddDelegatingHandler<TokenForwardingHandler>(true);

// 5) Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

var app = builder.Build();

// 6) Log CORS mode
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("CORS policy: {Mode}", env.IsDevelopment() ? "development" : "production");

// 7) Middleware pipeline
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();

app.UseCors("DynamicCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapHealthChecks("/health");
});

await app.UseOcelot();

app.Run();

public class TokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<TokenForwardingHandler> _logger;
    public TokenForwardingHandler(
          IHttpContextAccessor accessor,
          ILogger<TokenForwardingHandler> logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        var header = _accessor.HttpContext?
                          .Request.Headers["Authorization"]
                          .ToString();

        _logger.LogDebug(
            ">> TokenForwardingHandler – Header Authorization: {Auth}",
            header);

        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Authorization =
                AuthenticationHeaderValue.Parse(header);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
