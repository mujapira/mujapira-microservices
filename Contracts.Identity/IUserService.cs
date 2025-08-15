using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts.Identity
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetAll();
        Task<UserDto?> GetById(Guid id);
        Task<UserDto> Create(CreateUserDto dto);
        Task Update(Guid id, UpdateUserDto dto);
        Task Delete(Guid id);
        Task<UserDto?> ValidateCredentials(ValidateUserDto dto);

        Task PromoteToAdminByEmail(string targetEmail, string callerEmail);

    }
}
