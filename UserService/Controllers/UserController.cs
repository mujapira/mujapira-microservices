// src/UserService/Controllers/UsersController.cs
using Microsoft.AspNetCore.Mvc;
using UserService.Models;
using UserService.Services;

namespace UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    // GET /api/users
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();
        return Ok(users);
    }

    // GET /api/users/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user is null)
            return NotFound();
        return Ok(user);
    }

    // POST /api/users
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Email e senha são obrigatórios.");

        var created = await _userService.CreateAsync(dto.Email, dto.Password, dto.IsAdmin);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // PUT /api/users/{id}
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var existing = await _userService.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        existing.Email = dto.Email;
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            existing.Password = dto.Password; // o hash é aplicado dentro do serviço
        }
        existing.IsAdmin = dto.IsAdmin;

        await _userService.UpdateAsync(existing);
        return NoContent();
    }

    // DELETE /api/users/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _userService.DeleteAsync(id);
        return NoContent();
    }
}

// DTOs usados pelo controller
public record CreateUserDto(string Email, string Password, bool IsAdmin);
public record UpdateUserDto(string Email, string? Password, bool IsAdmin);
