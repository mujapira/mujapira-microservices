using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Contracts.Users;
using Contracts.Logs;
using UserService.Data;
using UserService.Models;
using Contracts.Common;

namespace UserService.Services;

public class UserService(CorpContext ctx, IKafkaProducer producer) : IUserService
{
    private readonly CorpContext _ctx = ctx;
    private readonly IKafkaProducer _producer = producer;

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

        await _producer.Produce(JsonSerializer.Serialize(logDto));

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
        await _producer.Produce(JsonSerializer.Serialize(logDto));

        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto> Create(CreateUserDto dto)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = dto.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Name = dto.Name,
            IsAdmin = dto.IsAdmin,
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
        await _producer.Produce(JsonSerializer.Serialize(logDto));

        return ToDto(user);
    }

    public async Task Update(Guid id, UpdateUserDto dto)
    {
        var user = await _ctx.Users.FindAsync(id)
                   ?? throw new KeyNotFoundException($"Usuário {id} não encontrado");

        user.Email = dto.Email;
        user.Name = dto.Name;
        user.IsAdmin = dto.IsAdmin;
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
        await _producer.Produce(JsonSerializer.Serialize(logDto));
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
            await _producer.Produce(JsonSerializer.Serialize(logDto));
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
        await _producer.Produce(JsonSerializer.Serialize(logDto));

        return isValid ? ToDto(user!) : null;
    }
}
