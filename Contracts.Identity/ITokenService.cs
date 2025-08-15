using System.Security.Claims;

namespace Contracts.Identity
{
    public interface ITokenService
    {
        string GenerateAccessToken(UserDto user);
        string GenerateRefreshToken();
        ClaimsPrincipal GetPrincipalFromToken(string token);
        string GetTokenId(string token);
    }
}
