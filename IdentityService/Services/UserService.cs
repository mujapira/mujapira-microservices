using Contracts.Common;
using Contracts.Identity;
using Contracts.Logs;
using Contracts.Mail;
using Humanizer;
using IdentityService.Data;
using IdentityService.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IdentityService.Services;

public class UserService(CorpContext ctx, IKafkaProducer producer, IConfiguration config) : IUserService
{
    private readonly CorpContext _ctx = ctx;
    private readonly IKafkaProducer _producer = producer;
    private readonly IConfiguration _config = config;
    private static UserDto ToDto(User u)
        => new(u.Id, u.Email, u.Name, u.IsAdmin);

    public async Task<IEnumerable<UserDto>> GetAll()
    {
        var users = await _ctx.Users
                              .AsNoTracking()
                              .ToListAsync();

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.UserService,
            Level: Contracts.Logs.LogLevel.Info,
            Message: "Listagem de usuários",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object> { ["Count"] = users.Count }
        );

        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), (logDto));

        return users.Select(ToDto);
    }

    public async Task<UserDto?> GetById(Guid id)
    {
        var user = await _ctx.Users.FindAsync(id);

        var logDto = new LogMessageDto(
             Source: RegisteredMicroservices.UserService,
            Level: Contracts.Logs.LogLevel.Info,
            Message: "Busca de usuário por ID",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["UserId"] = id,
                ["Found"] = user != null
            }
        );

        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), (logDto));

        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto> Create(CreateUserDto dto)
    {
        // normaliza e-mail
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("E-mail é obrigatório.");

        // checa duplicidade (e garante índice único no banco)
        var exists = await _ctx.Users.AsNoTracking().AnyAsync(u => u.Email == email);
        if (exists)
            throw new Exception("Problema ao criar usuário.");

        // verifica se este e-mail coincide com o admin "secreto"
        var adminEmail = _config["ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        var isAdmin = !string.IsNullOrWhiteSpace(adminEmail) && email == adminEmail;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Name = dto.Name,
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow
        };

        _ctx.Users.Add(user);
        await _ctx.SaveChangesAsync();

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.UserService,
            Level: Contracts.Logs.LogLevel.Info,
            Message: "Usuário criado",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["UserId"] = user.Id,
                ["Email"] = user.Email,
                ["IsAdmin"] = user.IsAdmin
            }
        );

        var createdUserEvent = new CreatedUserEventDto(
            Id: user.Id,
            Email: user.Email,
            Name: user.Name,
            IsAdmin: user.IsAdmin
        );

        _producer.ProduceFireAndForget(MailKafkaTopics.UserRegistered.GetTopicName(), createdUserEvent);
        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), logDto);

        return ToDto(user);
    }


    public async Task Update(Guid id, UpdateUserDto dto)
    {
        var user = await _ctx.Users.FindAsync(id)
                   ?? throw new KeyNotFoundException($"Usuário {id} não encontrado");

        user.Email = dto.Email;
        user.Name = dto.Name;
        if (!string.IsNullOrWhiteSpace(dto.Password))
            user.Password = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        _ctx.Users.Update(user);
        await _ctx.SaveChangesAsync();

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.UserService,
            Level: Contracts.Logs.LogLevel.Info,
            Message: "Usuário atualizado",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object> { ["UserId"] = id }
        );
        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), (logDto));
    }

    public async Task Delete(Guid id)
    {
        var user = await _ctx.Users.FindAsync(id);
        if (user != null)
        {
            _ctx.Users.Remove(user);
            await _ctx.SaveChangesAsync();

            var logDto = new LogMessageDto(
                 Source: RegisteredMicroservices.UserService,
                Level: Contracts.Logs.LogLevel.Info,
                Message: "Usuário deletado",
                Timestamp: DateTime.UtcNow,
                Metadata: new Dictionary<string, object> { ["UserId"] = id }
            );
            _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), (logDto));
        }
    }

    public async Task<UserDto?> ValidateCredentials(ValidateUserDto dto)
    {
        var user = await _ctx.Users
                             .AsNoTracking()
                             .FirstOrDefaultAsync(u => u.Email == dto.Email);

        var isValid = user != null &&
                      BCrypt.Net.BCrypt.Verify(dto.Password, user.Password);

        var logDto = new LogMessageDto(
             Source: RegisteredMicroservices.UserService,
            Level: isValid ? Contracts.Logs.LogLevel.Info : Contracts.Logs.LogLevel.Warn,
            Message: "Validação de credenciais",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["Email"] = dto.Email,
                ["Success"] = isValid
            }
        );
        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), (logDto));

        return isValid ? ToDto(user!) : null;
    }

    public async Task PromoteToAdminByEmail(string targetEmail, string callerEmail)
    {
        var email = (targetEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email é obrigatório.", nameof(targetEmail));

        var caller = (callerEmail ?? string.Empty).Trim().ToLowerInvariant();

        var adminEmail = (_config["ADMIN_EMAIL"] ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(caller) ||
            caller != adminEmail)
        {
            throw new UnauthorizedAccessException("Operação não permitida.");
        }

        var user = await _ctx.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
            throw new KeyNotFoundException("Usuário não encontrado.");

        if (user.IsAdmin)
        {
            var idempotentLog = new LogMessageDto(
                Source: RegisteredMicroservices.UserService,
                Level: Contracts.Logs.LogLevel.Info,
                Message: "Usuário já era admin",
                Timestamp: DateTime.UtcNow,
                Metadata: new Dictionary<string, object>
                {
                    ["TargetUserId"] = user.Id,
                    ["TargetEmail"] = user.Email,
                    ["CallerEmail"] = caller
                }
            );

            _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), idempotentLog);
            return;
        }

        user.IsAdmin = true;
        await _ctx.SaveChangesAsync();

        var logDto = new LogMessageDto(
            Source: RegisteredMicroservices.UserService,
            Level: Contracts.Logs.LogLevel.Info,
            Message: "Usuário promovido a admin",
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object>
            {
                ["TargetUserId"] = user.Id,
                ["TargetEmail"] = user.Email,
                ["CallerEmail"] = caller
            }
        );

        _producer.ProduceFireAndForget(LogKafkaTopics.Users.GetTopicName(), logDto);
    }
}
