using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProductionLine.Shared;

// 기존 로그인 클라이언트의 주소·토큰을 사용한다. 기존 공개 함수와 DTO는 변경하지 않는다.
public sealed class GroupChatApiClient(ProductionLineApiClient login) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = login.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ChatMessagePage> GetMessagesAsync(long afterId = 0, int limit = 100, CancellationToken token = default)
    {
        if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return SendAsync<ChatMessagePage>(HttpMethod.Get, $"api/chat/messages?afterId={afterId}&limit={limit}", null, token);
    }

    public Task<ChatMessageDto> SendMessageAsync(SendChatMessageRequest message, CancellationToken token = default)
        => SendAsync<ChatMessageDto>(HttpMethod.Post, "api/chat/messages", message, token);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(login.Token)) throw new InvalidOperationException("먼저 로그인해 주세요.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, token);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new InvalidOperationException("채팅 응답이 비어 있습니다.");
        ApiMessage? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiMessage>(Json, token); }
        catch (JsonException) { }
        var message = error?.Message ?? (response.StatusCode switch
        {
            HttpStatusCode.NotFound => "서버에 단체 채팅 기능이 없습니다. 최신 서버를 다시 실행해 주세요.",
            HttpStatusCode.Unauthorized => "로그인이 만료되었습니다. 앱을 닫고 다시 로그인해 주세요.",
            _ => $"채팅 서버 오류: {(int)response.StatusCode}"
        });
        throw new InvalidOperationException(message);
    }

    public void Dispose() => _http.Dispose();
}
