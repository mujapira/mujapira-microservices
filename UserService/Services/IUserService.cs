using UserService.Models;

namespace UserService.Services
{
    public interface IUserService
    {
        /// <summary>
        /// Retorna todos os usuários.
        /// </summary>
        Task<IEnumerable<User>> GetAllAsync();

        /// <summary>
        /// Retorna um usuário pelo seu Id, ou null caso não exista.
        /// </summary>
        Task<User?> GetByIdAsync(Guid id);

        /// <summary>
        /// Cria um novo usuário (com hash de senha) e retorna a entidade criada.
        /// </summary>
        Task<User> CreateAsync(string email, string password, bool isAdmin);

        /// <summary>
        /// Atualiza os dados de um usuário. 
        /// Se newPassword não for nulo ou vazio, ele rehashará a senha.
        /// </summary>
        Task UpdateAsync(User user, string? newPassword = null);

        /// <summary>
        /// Remove um usuário pelo seu Id.
        /// </summary>
        Task DeleteAsync(Guid id);
    }
}
