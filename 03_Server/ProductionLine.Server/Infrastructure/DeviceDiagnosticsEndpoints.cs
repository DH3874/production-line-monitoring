using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

internal static class DeviceDiagnosticsEndpoints
{
    public static void MapDeviceDiagnostics(this WebApplication app)
    {
        app.MapGet("/api/lines/{lineId:int}/diagnostics", (HttpContext context, int lineId, AppStore store,
            AuthService auth, DeviceDiagnosticsService diagnostics) =>
        {
            var session = auth.Get(context);
            if (session is null) return Results.Json(new ApiMessage("로그인이 필요합니다."), statusCode: 401);
            if (!session.CanAccessLine(lineId)) return Results.Json(new ApiMessage("접근 권한이 없습니다."), statusCode: 403);
            var line = store.FindLine(lineId);
            if (line is null) return Results.Json(new ApiMessage("라인을 찾을 수 없습니다."), statusCode: 404);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(diagnostics.Capture(line));
        });
    }
}
