using ProductionLine.Shared;
namespace _01_AdminApp.Services;
public interface IAdminAuthService { Task<UserDto> LoginAsync(string id, string password); }
public sealed class ApiAdminAuthService : IAdminAuthService
{
    private readonly ProductionLineApiClient _api;
    public ApiAdminAuthService(ProductionLineApiClient api) => _api = api;
    public Task<UserDto> LoginAsync(string id, string password) => _api.LoginAsync(id, password);
}
// Designer constructor only. Program always uses the real server services.
public sealed class MockAdminAuthService : IAdminAuthService
{
    public Task<UserDto> LoginAsync(string id, string password) =>
        throw new InvalidOperationException("Program.cs를 시작점으로 실행하세요.");
}
