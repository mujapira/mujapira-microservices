namespace Contracts.Users;
public record ValidateUserDto(
    string Email,
    string Password);