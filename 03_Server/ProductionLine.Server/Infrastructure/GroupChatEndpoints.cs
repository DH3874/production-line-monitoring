using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

public static class GroupChatEndpoints
{
    public static void MapGroupChat(this WebApplication app)
    {
        app.MapGet("/api/chat/messages", (HttpContext context, long? afterId, int? limit,
            AuthService auth, AppStore users, GroupChatStore chat) =>
        {
            var session = auth.Get(context);
            if (session is null) return Error(401, "로그인이 필요합니다. 앱을 닫고 다시 로그인해 주세요.");
            if (ActiveUser(users, session.UserId) is null) return Error(403, "사용 중지되거나 존재하지 않는 계정입니다.");
            if (afterId is < 0 || limit is < 1 or > 100) return Error(400, "조회 위치와 개수를 확인하세요. 개수는 1~100입니다.");
            return Results.Ok(chat.GetMessages(afterId ?? 0, limit ?? 100));
        });

        app.MapPost("/api/chat/messages", (HttpContext context, SendChatMessageRequest request,
            AuthService auth, AppStore users, GroupChatStore chat, ILogger<GroupChatStore> logger) =>
        {
            var session = auth.Get(context);
            if (session is null) return Error(401, "로그인이 필요합니다. 앱을 닫고 다시 로그인해 주세요.");
            var sender = ActiveUser(users, session.UserId);
            if (sender is null) return Error(403, "사용 중지되거나 존재하지 않는 계정입니다.");
            try { return Results.Ok(chat.AddMessage(sender, request)); }
            catch (ArgumentException ex) { return Error(400, ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError("Group chat storage failed: {ErrorType}", ex.GetType().Name);
                return Error(503, "채팅 이력을 저장하지 못했습니다. 서버 담당자에게 알려주세요. 같은 내용으로 재전송할 수 있습니다.");
            }
        });
    }

    private static UserDto? ActiveUser(AppStore users, string id) => users.GetUsers().FirstOrDefault(x => x.Id == id && x.IsActive);
    private static IResult Error(int status, string message) => Results.Json(new ApiMessage(message), statusCode: status);
}
