namespace Contracts.Identity;
public record ValidateUserDto(
    string Email,
    string Password);