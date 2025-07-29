using AuthService.Models;
using AuthService.Dtos;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthService.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserServiceClient _userClient;
        private readonly ITokenService _tokenSvc;
        private readonly AuthDbContext _db;
        private readonly AuthJwtSettings _jwt;

        public AuthService(IUserServiceClient userClient, ITokenService tokenSvc, AuthDbContext db, 
            IOptions<AuthJwtSettings> jwtOpts)
        {
            _userClient = userClient;
            _tokenSvc = tokenSvc;
            _db = db;
            _jwt = jwtOpts.Value;
        }

        public async Task<AuthResult> LoginAsync(string email, string password)
        {
            var user = await _userClient.ValidateUserCredentialsAsync(email, password);
            if (user == null)
                return new AuthResult { Success = false, Errors = new[] { "Invalid credentials" } };

            var accessToken = _tokenSvc.GenerateAccessToken(user);
            var refreshToken = _tokenSvc.GenerateRefreshToken();

            await _db.RefreshTokens.AddAsync(new AuthRefreshToken
            {
                Token = refreshToken,
                JwtId = _tokenSvc.GetTokenId(accessToken),
                CreationDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays),
                Used = false,
                Invalidated = false,
                UserId = user.Id
            });
            await _db.SaveChangesAsync();

            return new AuthResult { Success = true, AccessToken = accessToken, RefreshToken = refreshToken };
        }

        public async Task<AuthResult> RefreshTokenAsync(string token, string refreshToken)
        {
            var principal = _tokenSvc.GetPrincipalFromToken(token);
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);

            if (stored == null || stored.Invalidated || stored.Used || stored.ExpiryDate < DateTime.UtcNow || stored.JwtId != jti)
                return new AuthResult { Success = false, Errors = new[] { "Invalid refresh token" } };

            stored.Used = true;
            _db.Update(stored);
            await _db.SaveChangesAsync();

            var userId = Guid.Parse(principal.FindFirst(ClaimTypes.NameIdentifier).Value);
            // fetch minimal UserDto again
            var user = new UserDto { Id = userId, Email = principal.FindFirst(ClaimTypes.Email).Value, IsAdmin = principal.IsInRole("Admin") };

            var newAccess = _tokenSvc.GenerateAccessToken(user);
            var newRefresh = _tokenSvc.GenerateRefreshToken();

            await _db.RefreshTokens.AddAsync(new AuthRefreshToken
            {
                Token = newRefresh,
                JwtId = _tokenSvc.GetTokenId(newAccess),
                CreationDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpirationDays),
                Used = false,
                Invalidated = false,
                UserId = userId
            });
            await _db.SaveChangesAsync();

            return new AuthResult { Success = true, AccessToken = newAccess, RefreshToken = newRefresh };
        }
    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string[] Errors { get; set; }
    }
}