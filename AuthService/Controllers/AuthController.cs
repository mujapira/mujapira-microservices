using AuthService.Redis;
using Contracts.Auth;
using Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AuthService.Controllers;

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

    private CookieOptions BuildRefreshCookieOptions(bool secureFlag)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secureFlag,
            SameSite = SameSiteMode.Strict,
            Domain = _env.IsProduction()
                     ? ".mujapira.com"
                     : "localhost",
            Expires = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            Path = "/"
        };
    }

    private static readonly string[] _allowedOrigins = new[]
    {
        "https://mujapira.com",
        "https://www.mujapira.com",
        "http://localhost:3000"
    };

    private bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
            return false;

        return _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private object BuildAccessTokenResponse(string accessToken)
    {
        return new
        {
            accessToken = accessToken,
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

        Response.Cookies.Append("refreshToken", result.RefreshToken, BuildRefreshCookieOptions(secureFlag));
        return Ok(BuildAccessTokenResponse(result.AccessToken));
    }


    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue("refreshToken", out var oldRefreshToken))
            return BadRequest(new { message = "Refresh token não encontrado." });

        // rate limit por refresh token para evitar abuse
        var tokenKey = $"rl:refresh:token:{oldRefreshToken}";
        var (allowedToken, retryToken) = await _rateLimiter.TryAcquireAsync(tokenKey, 10, TimeSpan.FromMinutes(1));
        if (!allowedToken)
        {
            Response.Headers["Retry-After"] = retryToken.ToString();
            return StatusCode(429, new { message = "Muitas tentativas de refresh. Tente novamente mais tarde." });
        }

        var originHeader = Request.Headers["Origin"].FirstOrDefault()
                           ?? Request.Headers["Referer"].FirstOrDefault();
        if (!_env.IsDevelopment() && !IsAllowedOrigin(originHeader))
        {
            _logger.LogWarning("Refresh bloqueado por origem não permitida: {Origin}", originHeader);
            return BadRequest(new { message = "Requisição de origem não permitida." });
        }

        var refreshResult = await _auth.RefreshToken(new RefreshTokenRequest(oldRefreshToken));
        if (!refreshResult.Success)
        {
            return BadRequest(new { message = "Refresh token inválido." });
        }

        var isProd = _env.IsProduction();
        var secureFlag = HttpContext.Request.IsHttps || isProd;

        Response.Cookies.Append("refreshToken", refreshResult.RefreshToken, BuildRefreshCookieOptions(secureFlag));
        return Ok(BuildAccessTokenResponse(refreshResult.AccessToken));
    }


    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        // pega refresh token do body ou cookie
        string? refreshToken = request.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken) && Request.Cookies.TryGetValue("refreshToken", out var rtFromCookie))
        {
            refreshToken = rtFromCookie;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return BadRequest(new { message = "Nenhum refresh token fornecido para logout." });
        }

        await _auth.Logout(new LogoutRequest(refreshToken));

        // limpa cookie de refresh
        Response.Cookies.Append("refreshToken", "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(-1),
            Path = "/"
        });

        return NoContent();
    }
}
