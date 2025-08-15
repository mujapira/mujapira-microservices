namespace Contracts.Identity;
public record UpdateUserDto(
    string Email,
    string? Password,
    string Name);

public record PromoteByEmailRequest(string Email);