using System.Security.Claims;
using Contracts.Users;

namespace AuthService.Services
{
    public interface ITokenService
    {
        string GenerateAccessToken(UserDto user);
        string GenerateRefreshToken();
        ClaimsPrincipal GetPrincipalFromToken(string token);
        string GetTokenId(string token);
    }
}
