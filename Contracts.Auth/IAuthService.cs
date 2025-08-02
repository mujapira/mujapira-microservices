namespace Contracts.Auth;
public interface IAuthService
{
    Task<AuthResult> Login(LoginRequest request);
    Task<AuthResult> RefreshToken(RefreshTokenRequest refreshToken);
    Task Logout(LogoutRequest request);
}
