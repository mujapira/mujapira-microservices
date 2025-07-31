using System.Net;
using Contracts.Users;

namespace AuthService.Services;

public class UserService(HttpClient http) : IUserService
{
    private readonly HttpClient _http = http;

    public async Task<UserDto?> ValidateCredentials(ValidateUserDto dto)
    {
        var resp = await _http.PostAsJsonAsync("/users/validateCredentials", dto);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserDto>();
    }

    public async Task<UserDto?> GetById(Guid userId)
    {
        var resp = await _http.GetAsync($"/users/{userId}");
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserDto>();
    }

    public Task<IEnumerable<UserDto>> GetAll()
    {
        throw new NotImplementedException();
    }

    public Task<UserDto> Create(CreateUserDto dto)
    {
        throw new NotImplementedException();
    }

    public Task Update(Guid id, UpdateUserDto dto)
    {
        throw new NotImplementedException();
    }

    public Task Delete(Guid id)
    {
        throw new NotImplementedException();
    }
}
