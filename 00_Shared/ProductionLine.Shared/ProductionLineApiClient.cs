using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProductionLine.Shared;

public sealed class ProductionLineApiClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? Token { get; private set; }
    public UserDto? CurrentUser { get; private set; }
    public Uri BaseAddress => _http.BaseAddress!;

    public ProductionLineApiClient(string? baseUrl = null)
    {
        baseUrl ??= Environment.GetEnvironmentVariable("PRODUCTIONLINE_SERVER_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Portable package: edit one address file for both administrator and worker PCs.
            var settings = new[] { Path.Combine(AppContext.BaseDirectory, "서버주소.txt"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "서버주소.txt")) };
            var file = settings.FirstOrDefault(File.Exists);
            baseUrl = file == null ? "http://127.0.0.1:5050" : File.ReadAllText(file).Trim();
        }
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
            throw new ArgumentException("서버주소.txt에 http://서버IP:5050 형식의 주소를 입력하세요.");
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<UserDto> LoginAsync(string userId, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(userId, password));
        var login = await ReadAsync<LoginResponse>(response);
        Token = login.Token;
        CurrentUser = login.User;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return login.User;
    }

    public Task<List<LineStatusDto>> GetLinesAsync() => GetAsync<List<LineStatusDto>>("api/lines");
    public Task<List<LineHardwareStatusDto>> GetHardwareHistoryAsync(int lineId) =>
        GetAsync<List<LineHardwareStatusDto>>($"api/lines/{lineId}/hardware/history");
    public Task<RemoteLineSnapshotDto> GetRemoteLineAsync(int lineId) =>
        GetAsync<RemoteLineSnapshotDto>($"api/lines/{lineId}/remote");
    public async Task<WorkerHeartbeatResponse> SendWorkerHeartbeatAsync(int lineId, WorkerHeartbeatRequest request)
    {
        using var response = await _http.PostAsJsonAsync($"api/lines/{lineId}/remote/worker", request);
        return await ReadAsync<WorkerHeartbeatResponse>(response);
    }
    public async Task<RemoteCommandDto> CreateRemoteCommandAsync(int lineId, CreateRemoteCommandRequest request)
    {
        using var response = await _http.PostAsJsonAsync($"api/lines/{lineId}/remote/commands", request);
        return await ReadAsync<RemoteCommandDto>(response);
    }
    public async Task<RemoteCommandDto> CompleteRemoteCommandAsync(int lineId, Guid id, CompleteRemoteCommandRequest request)
    {
        using var response = await _http.PostAsJsonAsync($"api/lines/{lineId}/remote/commands/{id}/complete", request);
        return await ReadAsync<RemoteCommandDto>(response);
    }
    public async Task<LineHardwareStatusDto> UpdateLineHardwareAsync(int lineId, UpdateLineHardwareRequest request)
    {
        using var response = await _http.PostAsJsonAsync($"api/lines/{lineId}/hardware", request);
        return await ReadAsync<LineHardwareStatusDto>(response);
    }
    public Task<List<UserDto>> GetUsersAsync() => GetAsync<List<UserDto>>("api/users");
    public Task<List<IssueDto>> GetIssuesAsync() => GetAsync<List<IssueDto>>("api/issues");

    public Task<List<IssueMessageDto>> GetIssueMessagesAsync(Guid issueId, long? afterId = null) =>
        GetAsync<List<IssueMessageDto>>($"api/issues/{issueId}/messages" + (afterId.HasValue ? $"?afterId={afterId.Value}" : string.Empty));

    public async Task<IssueMessageDto> SendIssueMessageAsync(Guid issueId, string message)
    {
        using var response = await _http.PostAsJsonAsync($"api/issues/{issueId}/messages", new SendIssueMessageRequest(message));
        return await ReadAsync<IssueMessageDto>(response);
    }
    public Task<List<InspectionDto>> GetInspectionsAsync(int lineId) =>
        GetAsync<List<InspectionDto>>($"api/inspections?lineId={lineId}");

    public async Task<byte[]> GetInspectionImageAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"api/inspections/{inspectionId}/image", cancellationToken);
        if (!response.IsSuccessStatusCode) await ReadAsync<ApiMessage>(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/users", request);
        return await ReadAsync<UserDto>(response);
    }

    public async Task<UserDto> UpdateUserAsync(string userId, UpdateUserRequest request)
    {
        using var response = await _http.PutAsJsonAsync($"api/users/{Uri.EscapeDataString(userId)}", request);
        return await ReadAsync<UserDto>(response);
    }

    public async Task<InspectionDto> SimulateInspectionAsync(InspectionRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/inspections/simulate", request);
        return await ReadAsync<InspectionDto>(response);
    }

    public async Task<InspectionDto> SaveCameraInspectionAsync(CameraInspectionRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/inspections/camera", request);
        return await ReadAsync<InspectionDto>(response);
    }

    public async Task<IssueDto> CreateIssueAsync(CreateIssueRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/issues", request);
        return await ReadAsync<IssueDto>(response);
    }

    public async Task<IssueDto> UpdateIssueAsync(Guid issueId, UpdateIssueStatusRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/issues/{issueId}/status", request);
        return await ReadAsync<IssueDto>(response);
    }

    public async Task<AiConsultResponse> ConsultAiAsync(AiConsultRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/ai/consult", request);
        return await ReadAsync<AiConsultResponse>(response);
    }

    // 생산 검사 보고서(CSV 글자). 관리자만 받을 수 있습니다. (김하균 2026-10-23)
    public async Task<string> GetLineReportCsvAsync(int lineId, DateTimeOffset from, DateTimeOffset to)
    {
        using var response = await _http.GetAsync(
            $"api/reports/lines/{lineId}?from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}");
        if (!response.IsSuccessStatusCode) await ReadAsync<ApiMessage>(response); // 실패면 서버 안내 문구로 예외
        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose() => _http.Dispose();

    private async Task<T> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        return await ReadAsync<T>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))
                   ?? throw new InvalidOperationException("서버 응답이 비어 있습니다.");
        }

        var body = await response.Content.ReadAsStringAsync();
        try
        {
            var message = JsonSerializer.Deserialize<ApiMessage>(body, JsonOptions)?.Message;
            throw new InvalidOperationException(message ?? $"서버 오류: {(int)response.StatusCode}");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"서버 오류: {(int)response.StatusCode} {body}");
        }
    }
}
