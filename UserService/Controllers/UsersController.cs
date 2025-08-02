using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Contracts.Users;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(IUserService userService) : ControllerBase
{
    private readonly IUserService _userService = userService;

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
    [HttpPost]
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
}
