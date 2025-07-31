namespace Contracts.Users;
public record CreateUserDto(
    string Email,
    string Password,
    string Name,
    bool IsAdmin);