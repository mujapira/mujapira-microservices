using AuthService.Dtos;
using System.Security.Claims;

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
