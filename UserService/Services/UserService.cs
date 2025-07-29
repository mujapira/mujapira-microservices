using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

public class UserService : IUserService
{
    private readonly CorpContext _ctx;
    private readonly IKafkaProducer _producer;

    public UserService(CorpContext ctx, IKafkaProducer producer)
    {
        _ctx = ctx;
        _producer = producer;
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        var users = await _ctx.Users
                             .AsNoTracking()
                             .ToListAsync();

        var logEvent = new LogMessage()
        {
            Source = "UserService",
            Level = "INFO",
            Message = "Listagem de usuários",
            Metadata = new Dictionary<string, object>
            {
                { "Count", users.Count }
            },
            Timestamp = DateTime.UtcNow
        };
        await _producer.ProduceAsync(JsonSerializer.Serialize(logEvent));

        return users;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        var user = await _ctx.Users.FindAsync(id);

        var logEvent = new LogMessage()
        {
            Source = "UserService",
            Level = "INFO",
            Message = "Busca de usuário por ID",
            Metadata = new Dictionary<string, object>
            {
                { "UserId", id.ToString() },
                { "Found", (user != null).ToString() }
            },
            Timestamp = DateTime.UtcNow
        };
        await _producer.ProduceAsync(JsonSerializer.Serialize(logEvent));

        return user;
    }

    public async Task<User> CreateAsync(string email, string password, bool isAdmin)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow
        };

        _ctx.Users.Add(user);
        await _ctx.SaveChangesAsync();

        var logEvent = new LogMessage()
        {
            Source = "UserService",
            Level = "INFO",
            Message = "Usuário criado",
            Metadata = new Dictionary<string, object>
            {
                { "UserId", user.Id.ToString() },
                { "Email", user.Email },
                { "IsAdmin", user.IsAdmin.ToString() }
            },
            Timestamp = DateTime.UtcNow
        };
        await _producer.ProduceAsync(JsonSerializer.Serialize(logEvent));

        return user;
    }

    public async Task UpdateAsync(User user, string? newPassword = null)
    {
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        }

        _ctx.Users.Update(user);
        await _ctx.SaveChangesAsync();

        var logEvent = new LogMessage()
        {
            Source = "UserService",
            Level = "INFO",
            Message = "Usuário atualizado",
            Metadata = new Dictionary<string, object>
            {
                { "UserId", user.Id.ToString() }
            },
            Timestamp = DateTime.UtcNow
        };
        await _producer.ProduceAsync(JsonSerializer.Serialize(logEvent));
    }

    public async Task DeleteAsync(Guid id)
    {
        var user = await _ctx.Users.FindAsync(id);
        if (user is not null)
        {
            _ctx.Users.Remove(user);
            await _ctx.SaveChangesAsync();

            var logEvent = new LogMessage()
            {
                Source = "UserService",
                Level = "INFO",
                Message = "Usuário deletado",
                Metadata = new Dictionary<string, object>
                {
                    { "UserId", id.ToString() }
                },
                Timestamp = DateTime.UtcNow
            };
            await _producer.ProduceAsync(JsonSerializer.Serialize(logEvent));
        }
    }

    public async Task<User?> ValidateCredentialsAsync(string email, string password)
    {
        var user = await _ctx.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null) return null;

        return BCrypt.Net.BCrypt.Verify(password, user.Password)
               ? user
               : null;
    }
}
