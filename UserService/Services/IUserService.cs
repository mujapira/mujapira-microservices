using UserService.Models;

namespace UserService.Services
{
    public interface IUserService
    {

        Task<IEnumerable<User>> GetAllAsync();

        Task<User?> GetByIdAsync(Guid id);

        Task<User> CreateAsync(string email, string password, bool isAdmin);


        Task UpdateAsync(User user, string? newPassword = null);


        Task DeleteAsync(Guid id);

        Task<User?> ValidateCredentialsAsync(string email, string password);
    }
}
