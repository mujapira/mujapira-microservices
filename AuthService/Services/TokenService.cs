using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Contracts.Common;
using Contracts.Users;

namespace AuthService.Services
{

    public class TokenService(IOptions<JwtSettings> opts) : ITokenService
    {
        private readonly JwtSettings _jwt = opts.Value;

        public string GenerateAccessToken(UserDto user)
        {
            var jti = Guid.NewGuid().ToString();
            var roleValue = user.IsAdmin ? "Admin" : "User";

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, jti),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, roleValue)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                  issuer: _jwt.Issuer,
                  audience: _jwt.Audience,
                  claims: claims,
                  notBefore: DateTime.UtcNow,
                  expires: DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpirationMinutes),
                  signingCredentials: creds
             );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public ClaimsPrincipal GetPrincipalFromToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var valParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateLifetime = false,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret)),
                ValidateIssuerSigningKey = true
            };
            return handler.ValidateToken(token, valParams, out _);
        }

        public string GetTokenId(string token)
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Id;
        }
    }
}
