using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// [로그인 출입증 담당]
// - 로그인에 성공하면 Create가 "출입증(토큰)"이라는 긴 무작위 문자열을 만들어 앱에 줍니다.
// - 앱은 이후 요청마다 출입증을 같이 보내고, 각 API 창구는 Get으로 "누구인지, 무슨 권한인지" 확인합니다.
// - 출입증은 서버 메모리(_sessions)에만 있어서 서버를 다시 켜면 모두 사라집니다 → 다시 로그인 필요.
public sealed class AuthService
{
    // 출입증 보관함: 출입증 문자열 → (사용자, 만료 시각). 여러 요청이 동시에 써도 안전한 보관함
    private readonly ConcurrentDictionary<string, AuthSession> _sessions = new();
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AuthService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // 출입증 발급 (로그인 성공 시). 유효 시간 8시간
    public string Create(UserRecord user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new AuthSession(
            user.Id, user.Role, user.AllowedLineIds.ToArray(), DateTimeOffset.UtcNow.AddHours(8));
        return token;
    }

    // 출입증 확인 (모든 API 창구가 맨 처음 부름). null이면 로그인 필요(401)
    // 요청의 "Authorization: Bearer 출입증" 부분을 읽고, 만료·비활성화·권한을 DB에서 다시 확인합니다.
    public AuthSession? Get(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[7..].Trim();
        if (!_sessions.TryGetValue(token, out var session)) return null;

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        // 로그인 당시의 권한 대신 요청 시점의 DB 정보를 확인합니다.
        using var db = _contextFactory.CreateDbContext();
        var user = db.Users.AsNoTracking().SingleOrDefault(x => x.Id == session.UserId);
        if (user is null || !user.IsActive || !Enum.IsDefined(user.Role))
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        var lineIds = db.UserLineAccesses.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.LineId)
            .Select(x => x.LineId)
            .ToArray();

        // 통신 기록에 요청한 사용자를 남깁니다.
        context.Items[RequestLogItems.UserId] = user.Id;

        // 요청마다 만료 시간을 연장하지 않고 최초 로그인 만료를 유지합니다.
        return new AuthSession(user.Id, user.Role, lineIds, session.ExpiresAt);
    }
}

// 확인된 요청자의 정보 (아이디, 역할, 볼 수 있는 라인, 만료 시각)
// CanAccessLine: 관리자는 모든 라인, 작업자는 허용된 라인만 true
public sealed record AuthSession(
    string UserId, UserRole Role, IReadOnlyList<int> AllowedLineIds, DateTimeOffset ExpiresAt)
{
    public bool CanAccessLine(int lineId) => Role == UserRole.Admin || AllowedLineIds.Contains(lineId);
}
