namespace Contracts.Identity;
public record CreateUserDto(
    string Email,
    string Password,
    string Name);