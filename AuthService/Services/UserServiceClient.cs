
using AuthService.Dtos;

namespace AuthService.Services
{
    public class UserServiceClient : IUserServiceClient
    {
        private readonly HttpClient _httpClient;
        public UserServiceClient(HttpClient httpClient) => _httpClient = httpClient;

        public async Task<UserDto> ValidateUserCredentialsAsync(string email, string password)
        {
            var response = await _httpClient.PostAsJsonAsync("/users/validate", new { email, password });
            response.EnsureSuccessStatusCode();
            var userDto = await response.Content.ReadFromJsonAsync<UserDto>() ?? throw new InvalidOperationException("The response content is null.");
            return userDto;
        }
    }

}
