using Contracts.Common;
using Contracts.Identity;
using IdentityService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(IUserService userService, IConfiguration config) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly IConfiguration _config = config;

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idClaim, out var userId))
            return Unauthorized();

        var user = await _userService.GetById(userId);
        if (user is null) return NotFound();
        return Ok(user);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await _userService.GetAll();
        return Ok(users);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetById(Guid id)
    {
        var user = await _userService.GetById(id);
        if (user is null) return NotFound();
        return Ok(user);
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto)
    {
        var created = await _userService.Create(dto);
        return CreatedAtAction(
          nameof(GetById),
          new { id = created.Id },
          created
        );
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var existing = await _userService.GetById(id);
        if (existing is null) return NotFound();

        await _userService.Update(id, dto);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _userService.Delete(id);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("validateCredentials")]
    public async Task<ActionResult<UserDto>> ValidateCredentials([FromBody] ValidateUserDto dto)
    {
        var user = await _userService.ValidateCredentials(dto);
        if (user is null) return Unauthorized();
        return Ok(user);
    }


    [HttpPost("promoteByEmail")]
    public async Task<IActionResult> PromoteByEmail([FromBody] PromoteByEmailRequest req)
    {
        var callerEmail = (User.FindFirstValue(ClaimTypes.Email)
                           ?? User.FindFirstValue("email")
                           ?? "").Trim().ToLowerInvariant();

        var adminEmail = (_config["ADMIN_EMAIL"] ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(callerEmail) || string.IsNullOrWhiteSpace(adminEmail) || callerEmail != adminEmail)
            return Forbid();

        if (string.IsNullOrWhiteSpace(req?.Email)) return BadRequest("Email é obrigatório.");

        await _userService.PromoteToAdminByEmail(req.Email, callerEmail);

        return NoContent();
    }
}
