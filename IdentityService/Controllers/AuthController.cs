using Contracts.Common;
using Contracts.Identity;
using IdentityService.Redis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver.Linq;

namespace IdentityService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AuthController> _logger;
    private readonly JwtSettings _jwtSettings;
    private readonly IRateLimiter _rateLimiter;

    public AuthController(
        IAuthService auth,
        IWebHostEnvironment env,
        ILogger<AuthController> logger,
        IOptions<JwtSettings> jwtOptions,
        IRateLimiter rateLimiter)
    {
        _auth = auth;
        _env = env;
        _logger = logger;
        _jwtSettings = jwtOptions.Value;
        _rateLimiter = rateLimiter;
    }

    private static CookieOptions BuildAccessCookieOptions(HttpRequest req, bool isProd, TimeSpan lifetime)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = isProd || req.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.Add(lifetime),
            Path = "/",
            IsEssential = true
        };

        var host = req.Host.Host;
        if (host.EndsWith("mujapira.com", StringComparison.OrdinalIgnoreCase))
            opts.Domain = ".mujapira.com";

        return opts;
    }

    private static readonly string[] _allowedOrigins = new[]
    {
        "https://mujapira.com",
        "https://www.mujapira.com"
    };

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
            return false;

        return _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private object BuildAccessTokenResponse(string accessToken)
    {
        return new
        {
            accessToken,
            tokenType = "Bearer",
            expiresIn = _jwtSettings.AccessTokenExpirationMinutes
        };
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var emailKey = $"rl:login:email:{req.Email.ToLowerInvariant()}";
        var ipKey = $"rl:login:ip:{ip}";

        // verifica email
        var (allowedEmail, retryEmail) = await _rateLimiter.TryAcquireAsync(emailKey, 5, TimeSpan.FromMinutes(1));
        if (!allowedEmail)
        {
            Response.Headers["Retry-After"] = retryEmail.ToString();
            return StatusCode(429, new { message = "Muitas tentativas de login para esse email. Tente novamente em alguns segundos." });
        }

        // verifica IP
        var (allowedIp, retryIp) = await _rateLimiter.TryAcquireAsync(ipKey, 10, TimeSpan.FromMinutes(1));
        if (!allowedIp)
        {
            Response.Headers["Retry-After"] = retryIp.ToString();
            return StatusCode(429, new { message = "Muitas tentativas de login desse IP. Tente novamente em alguns segundos." });
        }

        var result = await _auth.Login(req);
        if (!result.Success)
        {
            _logger.LogWarning("Login falhou para email {Email}", req.Email);
            return Unauthorized(new { message = "Credenciais inválidas" });
        }

        var isProd = _env.IsProduction();
        var secureFlag = HttpContext.Request.IsHttps || isProd;

        // cookies
        Response.Cookies.Append("refreshToken", result.RefreshToken, BuildAccessCookieOptions(Request, secureFlag, TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays)));
        Response.Cookies.Append("accessToken", result.AccessToken, BuildAccessCookieOptions(Request, secureFlag, TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes)));

        // mantém compat no corpo
        return Ok(BuildAccessTokenResponse(result.AccessToken));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue("refreshToken", out var oldRefreshToken))
            return BadRequest(new { message = "Refresh token não encontrado." });
        var isProd = _env.IsProduction();

        // rate limit por refresh token
        var tokenKey = $"rl:refresh:token:{oldRefreshToken}";
        var (allowedToken, retryToken) = await _rateLimiter.TryAcquireAsync(tokenKey, 10, TimeSpan.FromMinutes(1));
        if (!allowedToken)
        {
            Response.Headers["Retry-After"] = retryToken.ToString();
            return StatusCode(429, new { message = "Muitas tentativas de refresh. Tente novamente mais tarde." });
        }

        var originHeader = Request.Headers["Origin"].FirstOrDefault()
                           ?? Request.Headers["Referer"].FirstOrDefault();
        if (isProd && !IsAllowedOrigin(originHeader))
        {
            _logger.LogWarning("Refresh bloqueado por origem não permitida: {Origin}", originHeader);
            return BadRequest(new { message = "Requisição de origem não permitida." });
        }

        var refreshResult = await _auth.RefreshToken(new RefreshTokenRequest(oldRefreshToken));
        if (!refreshResult.Success)
        {
            return BadRequest(new { message = "Refresh token inválido." });
        }

        var secureFlag = HttpContext.Request.IsHttps || isProd;

        Response.Cookies.Append("refreshToken", refreshResult.RefreshToken, BuildAccessCookieOptions(Request, secureFlag, TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays)));
        Response.Cookies.Append("accessToken", refreshResult.AccessToken, BuildAccessCookieOptions(Request, secureFlag, TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes)));

        return NoContent();
    }


    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!Request.Cookies.TryGetValue("refreshToken", out var oldRefreshToken)
            || string.IsNullOrWhiteSpace(oldRefreshToken))
        {
            return BadRequest(new { message = "Refresh token não encontrado para logout." });
        }

        var tokenKey = $"rl:refresh:token:{oldRefreshToken}";
        var (allowed, retryAfter) = await _rateLimiter.TryAcquireAsync(tokenKey, 10, TimeSpan.FromMinutes(1));
        if (!allowed)
        {
            Response.Headers["Retry-After"] = retryAfter.ToString();
            return StatusCode(429, new { message = "Muitas tentativas de logout. Tente novamente em alguns segundos." });
        }

        await _auth.Logout(new LogoutRequest(oldRefreshToken));

        var isProd = _env.IsProduction();

        var refreshOpts = BuildAccessCookieOptions(Request, isProd, TimeSpan.Zero);
        refreshOpts.Expires = DateTime.UtcNow.AddDays(-1);

        var accessOpts = BuildAccessCookieOptions(Request, isProd, TimeSpan.Zero);
        accessOpts.Expires = DateTime.UtcNow.AddDays(-1);

        Response.Cookies.Append("refreshToken", "", refreshOpts);
        Response.Cookies.Append("accessToken", "", accessOpts);

        return NoContent();
    }
}
