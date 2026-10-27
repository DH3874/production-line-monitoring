using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProductionLine.Shared;

namespace _01_AdminApp.Services
{
    public interface IUserService
    {
        Task<List<UserDto>> GetUsersAsync();
        Task<UserDto> CreateUserAsync(CreateUserRequest request);
        Task<UserDto> UpdateUserAsync(string id, UpdateUserRequest request);
    }

    internal static class UserInput
    {
        public static void Validate(string name, string? password, bool required)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("사용자 이름을 입력하세요.");
            if ((required || !string.IsNullOrEmpty(password)) &&
                (string.IsNullOrWhiteSpace(password) || password.Length < 6))
                throw new InvalidOperationException("비밀번호는 6자 이상 입력하세요.");
        }
    }

    // 이 객체를 화면 간에 공유하여 임시 변경 내용 유지
    public sealed class MockUserService : IUserService
    {
        private readonly List<UserDto> _users = new List<UserDto>
        {
            new UserDto("admin", "관리자", UserRole.Admin, true, new[] { 1, 2, 3, 4 }),
            new UserDto("worker1", "1라인 작업자", UserRole.User, true, new[] { 1 }),
            new UserDto("worker2", "2라인 작업자", UserRole.User, true, new[] { 2 }),
            new UserDto("worker3", "비활성 작업자", UserRole.User, false, new[] { 3 })
        };

        // 화면에는 비밀번호를 반환하지 않음. 개발용 메모리 저장소.
        private readonly Dictionary<string, string> _passwords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["worker1"] = "worker123!",
                ["worker2"] = "worker123!",
                ["worker3"] = "worker123!"
            };

        public Task<List<UserDto>> GetUsersAsync() =>
            Task.FromResult(new List<UserDto>(_users));

        public Task<UserDto> CreateUserAsync(CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Id))
                throw new InvalidOperationException("사용자 ID를 입력하세요.");
            UserInput.Validate(request.Name, request.Password, true);
            string id = request.Id.Trim();
            if (_users.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("이미 사용 중인 사용자 ID입니다.");
            var user = new UserDto(id, request.Name.Trim(), UserRole.User, true,
                request.AllowedLineIds.Distinct().ToArray());
            _users.Add(user);
            _passwords[id] = request.Password;
            return Task.FromResult(user);
        }

        public Task<UserDto> UpdateUserAsync(string id, UpdateUserRequest request)
        {
            UserInput.Validate(request.Name, request.NewPassword, false);
            int index = _users.FindIndex(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException("사용자를 찾을 수 없습니다.");
            if (_users[index].Role != UserRole.User)
                throw new InvalidOperationException("이 화면에서는 작업자 계정만 수정할 수 있습니다.");
            var updated = _users[index] with
            {
                Name = request.Name.Trim(),
                IsActive = request.IsActive,
                AllowedLineIds = request.AllowedLineIds.Distinct().ToArray()
            };
            _users[index] = updated;
            if (!string.IsNullOrEmpty(request.NewPassword))
                _passwords[id] = request.NewPassword;
            return Task.FromResult(updated);
        }
    }

    public sealed class ApiUserService : IUserService
    {
        private readonly ProductionLineApiClient _api;
        public ApiUserService(ProductionLineApiClient api) => _api = api;

        public Task<List<UserDto>> GetUsersAsync() => ServerCall.Run(() => _api.GetUsersAsync());

        public Task<UserDto> CreateUserAsync(CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Id))
                throw new InvalidOperationException("사용자 ID를 입력하세요.");
            UserInput.Validate(request.Name, request.Password, true);
            return ServerCall.Run(() => _api.CreateUserAsync(request));
        }

        public Task<UserDto> UpdateUserAsync(string id, UpdateUserRequest request)
        {
            UserInput.Validate(request.Name, request.NewPassword, false);
            return ServerCall.Run(() => _api.UpdateUserAsync(id, request));
        }
    }
}
