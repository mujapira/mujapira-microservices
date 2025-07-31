namespace Contracts.Users;
public record UserDto(
    Guid Id,
    string Email,
    string Name,
    bool IsAdmin);
