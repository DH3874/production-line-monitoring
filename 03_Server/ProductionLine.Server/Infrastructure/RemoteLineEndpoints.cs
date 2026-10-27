using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

public static class RemoteLineEndpoints
{
    public static void MapRemoteLineEndpoints(this WebApplication app)
    {
        IResult Error(int status, string message) => Results.Json(new ApiMessage(message), statusCode: status);
        IResult? Access(HttpContext c, int line, AuthService auth, AppStore store, UserRole? role, out AuthSession? session)
        {
            session = auth.Get(c);
            if (session == null) return Error(401, "로그인이 필요합니다.");
            if (!session.CanAccessLine(line) || role.HasValue && session.Role != role.Value) return Error(403, "접근 권한이 없습니다.");
            if (store.FindLine(line) is not { IsReal: true }) return Error(400, "대표 실제 라인에서만 사용할 수 있습니다.");
            return null;
        }
        app.MapGet("/api/lines/{lineId:int}/hardware/history", (HttpContext c, int lineId, AuthService auth, AppStore store, LineHardwareStore hardware) =>
        {
            var denied = Access(c, lineId, auth, store, null, out _);
            return denied ?? Results.Ok(hardware.History(lineId));
        });
        app.MapGet("/api/lines/{lineId:int}/remote", (HttpContext c, int lineId, AuthService auth, AppStore store, RemoteLineStore remote) =>
        {
            var denied = Access(c, lineId, auth, store, UserRole.Admin, out _);
            return denied ?? Results.Ok(remote.Snapshot(lineId));
        });
        app.MapPost("/api/lines/{lineId:int}/remote/worker", (HttpContext c, int lineId, WorkerHeartbeatRequest request, AuthService auth, AppStore store, RemoteLineStore remote) =>
        {
            var denied = Access(c, lineId, auth, store, UserRole.User, out var session);
            if (denied != null) return denied;
            try { return Results.Ok(remote.Heartbeat(lineId, session!.UserId, request)); }
            catch (ArgumentException ex) { return Error(400, ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, ex.Message); }
        });
        app.MapPost("/api/lines/{lineId:int}/remote/commands", (HttpContext c, int lineId, CreateRemoteCommandRequest request, AuthService auth, AppStore store, RemoteLineStore remote) =>
        {
            var denied = Access(c, lineId, auth, store, UserRole.Admin, out var session);
            if (denied != null) return denied;
            try { return Results.Ok(remote.Enqueue(lineId, session!.UserId, request)); }
            catch (ArgumentException ex) { return Error(400, ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, ex.Message); }
        });
        app.MapPost("/api/lines/{lineId:int}/remote/commands/{id:guid}/complete", (HttpContext c, int lineId, Guid id, CompleteRemoteCommandRequest request, AuthService auth, AppStore store, RemoteLineStore remote) =>
        {
            var denied = Access(c, lineId, auth, store, UserRole.User, out var session);
            if (denied != null) return denied;
            try { return Results.Ok(remote.Complete(lineId, session!.UserId, id, request)); }
            catch (ArgumentException ex) { return Error(400, ex.Message); }
            catch (InvalidOperationException ex) { return Error(409, ex.Message); }
        });
    }
}
