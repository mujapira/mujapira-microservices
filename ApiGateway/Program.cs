using Contracts.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Infrastructure.Claims.Parser;
using Ocelot.Middleware;
using Ocelot.Responses;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var env = builder.Environment;

// 1) URLs & Configuration
builder.WebHost.UseUrls("http://+:5000");
try
{
    builder.Configuration
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Falha carregando configuração: {ex.Message}");
    throw;
}

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

builder.Services.AddRoleClaimsParser();

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

app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Headers.ContainsKey("Authorization") &&
        ctx.Request.Cookies.TryGetValue("accessToken", out var access))
    {
        ctx.Request.Headers["Authorization"] = $"Bearer {access}";
    }
    await next();
});

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

public static class ClaimsParserExtension
{
    public static IServiceCollection AddRoleClaimsParser(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IClaimsParser, RoleClaimsParser>());
        return services;
    }
}

public class RoleClaimsParser : IClaimsParser
{
    private static readonly Dictionary<string, string> defaultClaims = GetClaimTypesConstantValues();

    public Response<string> GetValue(IEnumerable<Claim> claims, string key, string delimiter, int index)
    {
        Response<string> value = GetValue(claims, key);
        if (value.IsError)
        {
            return value;
        }

        if (string.IsNullOrEmpty(delimiter))
        {
            return value;
        }

        string[] array = value.Data.Split(delimiter.ToCharArray());
        if (array.Length <= index || index < 0)
        {
            return new ErrorResponse<string>(new CannotFindClaimError($"Cannot find claim for key: {key}, delimiter: {delimiter}, index: {index}"));
        }

        return new OkResponse<string>(array[index]);
    }

    public Response<List<string>> GetValuesByClaimType(IEnumerable<Claim> claims, string claimType)
    {
        return new OkResponse<List<string>>((from x in claims
                                             where GetClaimTypeValue(x.Type) == claimType.ToLower()
                                             select x.Value).ToList());
    }

    private static Response<string> GetValue(IEnumerable<Claim> claims, string key)
    {
        string[] array = (from c in claims
                          where GetClaimTypeValue(c.Type) == key.ToLower()
                          select c.Value).ToArray();
        if (array.Length != 0)
        {
            return new OkResponse<string>(new StringValues(array).ToString());
        }

        return new ErrorResponse<string>(new CannotFindClaimError("Cannot find claim for key: " + key));
    }

    private static string GetClaimTypeValue(string claim)
    {
        string claimType = claim;
        if (defaultClaims.TryGetValue(claimType, out string? claimName))
        {
            claimType = claimName;
        }

        return claimType.ToLower();
    }

    private static Dictionary<string, string> GetClaimTypesConstantValues()
    {
        Type type = typeof(ClaimTypes);
        FieldInfo[] fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static);
        return fieldInfos.Where(fi => fi.IsLiteral && !fi.IsInitOnly)
            .ToDictionary(fi => fi.GetValue(null)!.ToString()!, fi => fi.Name);
    }
}

