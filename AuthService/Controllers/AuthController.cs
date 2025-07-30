using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var res = await _auth.LoginAsync(req.Email, req.Password);
        if (!res.Success)
            return Unauthorized(res.Errors);

        return Ok(new { accessToken = res.AccessToken, refreshToken = res.RefreshToken });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var res = await _auth.RefreshTokenAsync(req.AccessToken, req.RefreshToken);
        if (!res.Success) return BadRequest(res.Errors);
        return Ok(new { accessToken = res.AccessToken, refreshToken = res.RefreshToken });
    }
}

public class LoginRequest { public string Email { get; set; } public string Password { get; set; } }
public class RefreshRequest { public string AccessToken { get; set; } public string RefreshToken { get; set; } }
