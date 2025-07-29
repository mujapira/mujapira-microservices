using AuthService.Dtos;


namespace AuthService.Services
{
    public interface IUserServiceClient
    {
        Task<UserDto> ValidateUserCredentialsAsync(string email, string password);
    }
}


