namespace Contracts.Identity;
public record AuthResult(
    bool Success,
    string AccessToken,
    string RefreshToken,
    string[]? Errors = null)
{
    public static AuthResult SuccessResult(string at, string rt)
        => new(true, at, rt);
    public static AuthResult Failure(params string[] errors)
        => new(false, string.Empty, string.Empty, errors);
}