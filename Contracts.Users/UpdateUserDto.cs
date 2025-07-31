namespace Contracts.Users;
public record UpdateUserDto(
    string Email,
    string? Password,
    string Name,
    bool IsAdmin);