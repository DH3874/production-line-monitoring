using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProductionLine.Shared;

namespace ProductionLine.UserApp;

/// <summary>기존 인증과 DTO를 사용하고 AI 상담 요청의 대기 시간만 늘린다.</summary>
internal static class AiConsultationApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(35) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<AiConsultResponse> ConsultAsync(ProductionLineApiClient api, AiConsultRequest question,
        CancellationToken cancellationToken = default)
        => await SendAsync<AiConsultResponse>(api, HttpMethod.Post, "api/ai/consult", question, cancellationToken);

    internal static Task<AiConsultResponse> ChatAsync(ProductionLineApiClient api, AiChatRequest question,
        CancellationToken cancellationToken = default)
        => SendAsync<AiConsultResponse>(api, HttpMethod.Post, "api/ai/chat", question, cancellationToken);

    internal static Task<AiChatStatus> GetStatusAsync(ProductionLineApiClient api, CancellationToken cancellationToken = default)
        => SendAsync<AiChatStatus>(api, HttpMethod.Get, "api/ai/chat/status", null, cancellationToken);

    internal static Task<DeviceDiagnosticsSnapshot> GetDiagnosticsAsync(ProductionLineApiClient api, int lineId,
        CancellationToken cancellationToken = default)
        => SendAsync<DeviceDiagnosticsSnapshot>(api, HttpMethod.Get, $"api/lines/{lineId}/diagnostics", null, cancellationToken);

    private static async Task<T> SendAsync<T>(ProductionLineApiClient api, HttpMethod method, string path, object? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(api.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        if (payload is not null) request.Content = JsonContent.Create(payload, options: Json);
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ApiMessage? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiMessage>(Json, cancellationToken); }
            catch (JsonException) { }
            throw new HttpRequestException(error?.Message ?? (response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "서버를 최신 빌드로 다시 실행해 주세요. 요청한 상담·장비 상태 API를 찾지 못했습니다."
                : "AI 상담 요청에 실패했습니다."), null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new InvalidOperationException("AI 상담 응답을 읽을 수 없습니다.");
    }
}
