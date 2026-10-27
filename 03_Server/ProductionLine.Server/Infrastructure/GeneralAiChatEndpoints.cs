using ProductionLine.AiAssistant;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

internal static class GeneralAiChatEndpoints
{
    public static void MapGeneralAiChat(this WebApplication app)
    {
        app.MapGet("/api/ai/chat/status", (HttpContext context, AuthService auth, AiProviderOptions options) =>
        {
            if (auth.Get(context) is null) return Error(401, "로그인이 필요합니다.");
            // Configuration readiness is not a live call to the external provider.
            return Results.Ok(new AiChatStatus(options.IsConfigured, options.Provider.ToString()));
        });
        app.MapPost("/api/ai/chat", async (HttpContext context, AiChatRequest request,
            AppStore store, AuthService auth, AiConsultationService assistant, DeviceDiagnosticsService diagnostics) =>
        {
            var session = auth.Get(context);
            if (session is null) return Error(401, "로그인이 필요합니다.");
            if (request.LineId <= 0 || request.ConversationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4000)
                return Error(400, "상담 라인·대화 번호와 1~4000자 질문을 확인하세요.");
            if (!session.CanAccessLine(request.LineId)) return Error(403, "접근 권한이 없습니다.");
            if (!store.GetLines().Any(line => line.Id == request.LineId)) return Error(404, "상담할 라인을 찾을 수 없습니다.");
            var now = DateTimeOffset.UtcNow;
            // Reuse manual/safety/history logic with an in-memory context only.
            // This is never registered as a production incident or saved to the issue DB.
            var contextOnly = new IssueDto(request.ConversationId, request.LineId, IssueType.ProductionLine,
                "일반 공정 상담", "등록된 문제가 없는 일반 상담입니다. 사용자가 질문한 증상을 기준으로 안내하며 실제 장비 관측은 별도 diagnostics 자료의 수신 시각과 확인 범위를 따릅니다.",
                IssueStatus.Occurred, session.UserId, null, now, now);
            return Results.Ok(await assistant.ConsultWithDiagnosticsAsync(session.UserId, contextOnly, request.Question, diagnostics.Capture(store.FindLine(request.LineId)!), context.RequestAborted));
        });
    }

    private static IResult Error(int status, string message) => Results.Json(new ApiMessage(message), statusCode: status);
}
