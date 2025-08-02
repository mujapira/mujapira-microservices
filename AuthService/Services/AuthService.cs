using AuthService.Models;
using Contracts.Auth;
using Contracts.Common;
using Contracts.Logs;
using Contracts.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

namespace AuthService.Services
{
    public class AuthService(
        IUserService userService,
        ITokenService tokenService,
        AuthDbContext db,
        IOptions<JwtSettings> jwtOpts,
        IKafkaProducer producer) : IAuthService
    {
        private readonly IUserService _userService = userService;
        private readonly ITokenService _tokenService = tokenService;
        private readonly AuthDbContext _db = db;
        private readonly JwtSettings _jwt = jwtOpts.Value;
        private readonly IKafkaProducer _producer = producer;

        public async Task<AuthResult> Login(LoginRequest request)
        {
            var validateDto = new ValidateUserDto(request.Email, request.Password);
            var user = await _userService.ValidateCredentials(validateDto);
            if (user is null)
            {
                var logDto = new LogMessageDto(
                    Source: RegisteredMicroservices.AuthService,
                    Level: Contracts.Logs.LogLevel.Warn,
                    Message: "Login falhou: credenciais inválidas",
                    Timestamp: DateTime.UtcNow,
                    Metadata: new Dictionary<string, object>
                    {
                        ["Email"] = request.Email
                    }
                );
                await _producer.Produce(JsonSerializer.Serialize(logDto));
                return AuthResult.Failure("Credenciais inválidas");
            }

            var access = _tokenService.GenerateAccessToken(user);
            var refresh = _tokenService.GenerateRefreshToken();
            var now = DateTime.UtcNow;

            await _db.RefreshTokens.AddAsync(new AuthRefreshToken
            {
                Token = refresh,
                JwtId = _tokenService.GetTokenId(access),
                CreationDate = now,
                ExpiryDate = now.AddDays(_jwt.RefreshTokenExpirationDays),
                Used = false,
                Invalidated = false,
                UserId = user.Id
            });
            await _db.SaveChangesAsync();

            var successLog = new LogMessageDto(
                Source: RegisteredMicroservices.AuthService,
                Level: Contracts.Logs.LogLevel.Info,
                Message: "Login bem-sucedido",
                Timestamp: now,
                Metadata: new Dictionary<string, object>
                {
                    ["UserId"] = user.Id,
                    ["Email"] = user.Email
                }
            );
            await _producer.Produce(JsonSerializer.Serialize(successLog));

            return AuthResult.SuccessResult(access, refresh);
        }

        public async Task<AuthResult> RefreshToken(RefreshTokenRequest req)
        {
            // valida o refresh token no DB
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);
            if (stored == null || stored.Invalidated || stored.Used || stored.ExpiryDate < DateTime.UtcNow)
                return AuthResult.Failure("Refresh token inválido");

            // marca como usado
            stored.Used = true;
            _db.RefreshTokens.Update(stored);
            await _db.SaveChangesAsync();

            // recupera dados do usuário do próprio registro
            var userDto = await _userService.GetById(stored.UserId)
                          ?? throw new InvalidOperationException("Usuário não encontrado");

            // gera novos tokens
            var newAccess = _tokenService.GenerateAccessToken(userDto);
            var newRefresh = _tokenService.GenerateRefreshToken();
            var now = DateTime.UtcNow;

            // persiste novo refresh
            await _db.RefreshTokens.AddAsync(new AuthRefreshToken
            {
                Token = newRefresh,
                JwtId = _tokenService.GetTokenId(newAccess),
                CreationDate = now,
                ExpiryDate = now.AddDays(_jwt.RefreshTokenExpirationDays),
                Used = false,
                Invalidated = false,
                UserId = userDto.Id
            });
            await _db.SaveChangesAsync();

            return AuthResult.SuccessResult(newAccess, newRefresh);
        }

        public async Task Logout(LogoutRequest request)
        {
            var stored = await _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (stored is not null)
            {
                stored.Invalidated = true;
                _db.RefreshTokens.Update(stored);
                await _db.SaveChangesAsync();

                var logDto = new LogMessageDto(
                    Source: RegisteredMicroservices.AuthService,
                    Level: Contracts.Logs.LogLevel.Info,
                    Message: "Logout realizado",
                    Timestamp: DateTime.UtcNow,
                    Metadata: new Dictionary<string, object>
                    {
                        ["UserId"] = stored.UserId
                    }
                );
                await _producer.Produce(JsonSerializer.Serialize(logDto));
            }
        }
    }
}
