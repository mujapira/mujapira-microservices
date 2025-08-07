using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Contracts.Users
{
    public record CreatedUserEventDto(Guid Id, string Email, string Name, bool IsAdmin);
}
