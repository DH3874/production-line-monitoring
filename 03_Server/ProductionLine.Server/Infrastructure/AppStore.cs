using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// [저장 담당] DB에 저장하고 꺼내는 일은 모두 이 클래스가 합니다. (예전 JSON 파일 대신 SQLite 사용)
// - Program.cs의 API 창구가 이 클래스의 함수를 부릅니다. 예: 문제 등록 창구 → AddIssue
// - 함수마다 CreateDbContext()로 DB 연결을 잠깐 열고, 끝나면 using이 자동으로 닫습니다.
// - 문제 상태 순서(발생 → 확인 → 조치 중 → 완료 요청 → 종결)를 검사하는 곳도 여기(ValidateIssueTransition)입니다.
public sealed class AppStore
{
    // 여러 요청이 동시에 저장하려고 할 때 하나씩 차례로 처리하게 하는 자물쇠 (lock(_gate) 부분)
    private readonly object _gate = new();
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly string _imageDirectory;

    public AppStore(IDbContextFactory<AppDbContext> contextFactory, IHostEnvironment environment)
    {
        _contextFactory = contextFactory;
        _imageDirectory = Path.Combine(environment.ContentRootPath, "data", "inspection-images");
        Directory.CreateDirectory(_imageDirectory);
    }

    // ===== 사용자·계정 =====

    // 로그인: 아이디가 있고, 사용 가능 계정이고, 비밀번호가 맞으면 사용자 정보를 돌려줌 (틀리면 null)
    public UserRecord? ValidateLogin(string id, string password)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(password)) return null;
        using var db = _contextFactory.CreateDbContext();
        var user = FindUser(db, id);
        return user is { IsActive: true } && PasswordHasher.Verify(password, user.PasswordHash)
            ? ToRecord(db, user)
            : null;
    }

    // 사용자 목록 (관리자 화면용) — 각 사용자가 볼 수 있는 라인 번호도 함께
    public IReadOnlyList<UserDto> GetUsers()
    {
        using var db = _contextFactory.CreateDbContext();
        var permissions = db.UserLineAccesses.AsNoTracking().ToList();
        return db.Users.AsNoTracking().OrderBy(x => x.Id).ToList()
            .Select(x => new UserDto(x.Id, x.Name, x.Role, x.IsActive,
                permissions.Where(p => p.UserId == x.Id).Select(p => p.LineId).Order().ToList()))
            .ToList();
    }

    // 사용자 추가 (관리자만) — 새 계정은 항상 작업자로 만들어짐. 비밀번호는 암호화해서 저장
    public UserDto AddUser(CreateUserRequest request)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
                throw new ArgumentException("ID, 이름, 6자 이상의 비밀번호를 입력하세요.");

            using var db = _contextFactory.CreateDbContext();
            var id = request.Id.Trim();
            if (!IsValidUserId(id))
                throw new ArgumentException("사용자 ID는 영어·숫자·_·-만 사용해 3~20자로 입력하세요. (한글·전각 문자 불가)");
            if (FindUser(db, id) is not null)
                throw new InvalidOperationException("이미 사용 중인 사용자 ID입니다.");

            var lineIds = ValidateLineIds(db, request.AllowedLineIds);
            var user = new UserEntity
            {
                Id = id,
                Name = request.Name.Trim(),
                Role = UserRole.User,
                PasswordHash = PasswordHasher.Hash(request.Password),
                IsActive = true
            };
            db.Users.Add(user);
            db.UserLineAccesses.AddRange(lineIds.Select(lineId => new UserLineAccessEntity
            {
                UserId = user.Id,
                LineId = lineId
            }));
            db.SaveChanges();
            return new UserDto(user.Id, user.Name, user.Role, user.IsActive, lineIds);
        }
    }

    // 사용자 수정 (관리자만) — 이름, 사용 가능 여부, 볼 수 있는 라인, 새 비밀번호(선택)
    // 비활성화하거나 라인을 빼면 AuthService가 다음 요청부터 바로 막음
    public UserDto UpdateUser(string id, UpdateUserRequest request)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("이름을 입력하세요.");
            if (!string.IsNullOrWhiteSpace(request.NewPassword) && request.NewPassword.Length < 6)
                throw new ArgumentException("비밀번호는 6자 이상이어야 합니다.");

            using var db = _contextFactory.CreateDbContext();
            var user = FindUser(db, id) ?? throw new KeyNotFoundException("사용자를 찾을 수 없습니다.");
            var lineIds = ValidateLineIds(db, request.AllowedLineIds);
            user.Name = request.Name.Trim();
            user.IsActive = request.IsActive;
            if (!string.IsNullOrWhiteSpace(request.NewPassword))
                user.PasswordHash = PasswordHasher.Hash(request.NewPassword);

            var current = db.UserLineAccesses.Where(x => x.UserId == user.Id).ToList();
            var existingIds = current.Select(x => x.LineId).ToHashSet();
            db.UserLineAccesses.RemoveRange(current.Where(x => !lineIds.Contains(x.LineId)));
            db.UserLineAccesses.AddRange(lineIds.Where(x => !existingIds.Contains(x))
                .Select(lineId => new UserLineAccessEntity { UserId = user.Id, LineId = lineId }));
            db.SaveChanges();
            return new UserDto(user.Id, user.Name, user.Role, user.IsActive, lineIds);
        }
    }

    // ===== 라인·검사 =====

    // 라인 목록 — allowedLineIds를 주면 그 라인만 (작업자), 안 주면 전체 (관리자)
    public IReadOnlyList<LineStatusDto> GetLines(IEnumerable<int>? allowedLineIds = null)
    {
        using var db = _contextFactory.CreateDbContext();
        var query = db.Lines.AsNoTracking();
        if (allowedLineIds is not null)
        {
            var ids = allowedLineIds.Distinct().ToArray();
            query = query.Where(x => ids.Contains(x.Id));
        }
        return query.OrderBy(x => x.Id).ToList().Select(ToDto).ToList();
    }

    public LineStatusDto? FindLine(int id)
    {
        using var db = _contextFactory.CreateDbContext();
        var line = db.Lines.AsNoTracking().SingleOrDefault(x => x.Id == id);
        return line is null ? null : ToDto(line);
    }

    // 검사 결과 저장 — 검사 한 줄 추가 + 그 라인의 정상/불량/판정 불가 개수 +1 을 한 번에 저장
    public InspectionDto AddInspection(InspectionDto inspection, byte[]? image = null)
    {
        lock (_gate)
        {
            using var db = _contextFactory.CreateDbContext();
            // 같은 검사 번호(앱의 요청 번호)가 다시 오면 새로 저장·집계하지 않고 처음 기록을 돌려줌 (김하균)
            if (db.Inspections.AsNoTracking().SingleOrDefault(x => x.Id == inspection.Id) is { } existing)
                return ToDto(existing);
            inspection = inspection with { HasImage = image is not null };
            var imagePath = Path.Combine(_imageDirectory, inspection.Id.ToString("N") + ".jpg");
            if (image is not null) File.WriteAllBytes(imagePath, image);
            var line = db.Lines.Find(inspection.LineId)
                ?? throw new ArgumentException("존재하지 않는 라인입니다.");
            try
            {
                db.Inspections.Add(new InspectionEntity
                {
                    Id = inspection.Id,
                    LineId = inspection.LineId,
                    Result = inspection.Result,
                    Reason = inspection.Reason,
                    InspectedAt = inspection.InspectedAt,
                    DetectionConfidence = inspection.DetectionConfidence,
                    HasImage = inspection.HasImage,
                    PhoneModel = inspection.PhoneModel
                });
                if (inspection.Result == InspectionResultType.Normal) line.NormalCount++;
                else if (inspection.Result == InspectionResultType.Defect) line.DefectCount++;
                else if (inspection.Result == InspectionResultType.Undetermined) line.UndeterminedCount++;
                line.InspectionStatus = inspection.Result == InspectionResultType.Undetermined
                    ? inspection.DetectionConfidence.HasValue ? "휴대폰 검출 완료 · 품질 판정 미지원" : "검사 확인 필요"
                    : "검사 중";
                line.UpdatedAt = inspection.InspectedAt;
                db.SaveChanges();
                return inspection;
            }
            catch
            {
                if (image is not null && File.Exists(imagePath)) File.Delete(imagePath);
                throw;
            }
        }
    }

    // 검사 이력 — 최근 것부터 100건
    public IReadOnlyList<InspectionDto> GetInspections(int lineId)
    {
        using var db = _contextFactory.CreateDbContext();
        // SQLite의 DateTimeOffset 정렬 제한을 피하고 실제 시각 기준으로 정렬합니다.
        return db.Inspections.AsNoTracking().Where(x => x.LineId == lineId)
            .AsEnumerable().OrderByDescending(x => x.InspectedAt).ThenBy(x => x.Id)
            .Take(100).Select(ToDto).ToList();
    }

    public InspectionDto? FindInspection(Guid id)
    {
        using var db = _contextFactory.CreateDbContext();
        var inspection = db.Inspections.AsNoTracking().SingleOrDefault(x => x.Id == id);
        return inspection is null ? null : ToDto(inspection);
    }

    public byte[]? GetInspectionImage(Guid id)
    {
        using var db = _contextFactory.CreateDbContext();
        if (db.Inspections.AsNoTracking().SingleOrDefault(x => x.Id == id) is not { HasImage: true }) return null;
        var path = Path.Combine(_imageDirectory, id.ToString("N") + ".jpg");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    // ===== 문제 =====

    // 문제 등록 — 처음 상태는 항상 "발생(Occurred)"
    public IssueDto AddIssue(CreateIssueRequest request, string userId)
    {
        lock (_gate)
        {
            using var db = _contextFactory.CreateDbContext();
            if (string.IsNullOrWhiteSpace(request.Title))
                throw new ArgumentException("문제 제목을 입력하세요.");
            if (!Enum.IsDefined(request.Type))
                throw new ArgumentException("올바른 문제 종류를 선택하세요.");
            if (!db.Lines.Any(x => x.Id == request.LineId))
                throw new ArgumentException("존재하지 않는 라인입니다.");
            var creator = FindUser(db, userId)
                ?? throw new ArgumentException("등록한 사용자를 찾을 수 없습니다.");
            var now = DateTimeOffset.Now;
            var issue = new IssueEntity
            {
                Id = Guid.NewGuid(),
                LineId = request.LineId,
                Type = request.Type,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim() ?? "",
                Status = IssueStatus.Occurred,
                CreatedBy = creator.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Issues.Add(issue);
            db.SaveChanges();
            return ToDto(issue);
        }
    }

    // 문제 상태 변경 — 순서 검사(ValidateIssueTransition) 통과 시 상태를 바꾸고 변경 이력을 함께 저장
    // 변경자는 윈폼 입력이 아니라 인증된 서버 세션에서 전달합니다.
    public IssueDto UpdateIssue(Guid id, UpdateIssueStatusRequest request, string changedBy)
    {
        lock (_gate)
        {
            using var db = _contextFactory.CreateDbContext();
            var issue = db.Issues.Find(id) ?? throw new KeyNotFoundException("문제를 찾을 수 없습니다.");
            var actor = FindUser(db, changedBy)
                ?? throw new ArgumentException("변경한 사용자를 찾을 수 없습니다.");
            if (!Enum.IsDefined(request.Status))
                throw new ArgumentException("올바른 문제 상태를 선택하세요.");

            if (!actor.IsActive || !Enum.IsDefined(actor.Role))
                throw new UnauthorizedAccessException("사용할 수 없는 계정입니다.");
            if (actor.Role != UserRole.Admin && !db.UserLineAccesses.Any(
                    x => x.UserId == actor.Id && x.LineId == issue.LineId))
                throw new UnauthorizedAccessException("이 라인의 문제를 변경할 권한이 없습니다.");

            ValidateIssueTransition(issue, request, actor.Role);

            var now = DateTimeOffset.Now;
            db.IssueHistories.Add(new IssueHistoryEntity
            {
                Id = Guid.NewGuid(),
                IssueId = issue.Id,
                PreviousStatus = issue.Status,
                NewStatus = request.Status,
                ChangedBy = actor.Id,
                ChangedAt = now,
                ActionNote = request.ActionNote ?? ""
            });
            issue.Status = request.Status;
            issue.ActionNote = request.ActionNote;
            issue.UpdatedAt = now;
            // 현재 상태와 변경 이력을 함께 저장합니다.
            db.SaveChanges();
            return ToDto(issue);
        }
    }

    public IssueDto? FindIssue(Guid id)
    {
        using var db = _contextFactory.CreateDbContext();
        var issue = db.Issues.AsNoTracking().SingleOrDefault(x => x.Id == id);
        return issue is null ? null : ToDto(issue);
    }

    // 문제 목록 — 최근 등록한 것부터. allowedLineIds를 주면 그 라인 문제만 (작업자)
    public IReadOnlyList<IssueDto> GetIssues(IEnumerable<int>? allowedLineIds = null)
    {
        using var db = _contextFactory.CreateDbContext();
        var query = db.Issues.AsNoTracking();
        if (allowedLineIds is not null)
        {
            var ids = allowedLineIds.Distinct().ToArray();
            query = query.Where(x => ids.Contains(x.LineId));
        }
        return query.AsEnumerable().OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id).Select(ToDto).ToList();
    }

    // ===== 문의 메시지 (작업자 ↔ 관리자) =====

    // 메시지 저장 — 1~2,000자, 종결된 문제에는 저장 안 됨
    // 보낸 사람은 윈폼 입력이 아니라 인증된 서버 세션에서 전달합니다.
    public IssueMessageDto AddIssueMessage(Guid issueId, string senderId, string? message)
    {
        lock (_gate)
        {
            var text = message?.Trim() ?? "";
            if (text.Length is 0 or > 2000)
                throw new ArgumentException("메시지는 1~2,000자로 입력하세요.");

            using var db = _contextFactory.CreateDbContext();
            var issue = db.Issues.Find(issueId) ?? throw new KeyNotFoundException("문제를 찾을 수 없습니다.");
            // 종결된 문제는 읽기 전용 이력으로 보존합니다.
            if (issue.Status == IssueStatus.Closed)
                throw new InvalidOperationException("종결된 문제에는 메시지를 남길 수 없습니다.");
            var sender = FindUser(db, senderId)
                ?? throw new ArgumentException("보낸 사용자를 찾을 수 없습니다.");
            if (issue.Status != IssueStatus.InProgress)
                throw new InvalidOperationException("조치 중인 문제에서만 채팅할 수 있습니다.");

            var entity = new IssueMessageEntity
            {
                IssueId = issue.Id,
                SenderId = sender.Id,
                // 나중에 이름이 바뀌어도 보낸 당시 이름과 역할을 남깁니다.
                SenderName = sender.Name,
                SenderRole = sender.Role,
                Message = text,
                CreatedAt = DateTimeOffset.Now
            };
            db.IssueMessages.Add(entity);
            db.SaveChanges();
            return ToDto(entity);
        }
    }

    // 메시지 읽기 — afterId를 주면 그 번호보다 새 메시지만 (앱의 5초 확인용)
    public IReadOnlyList<IssueMessageDto> GetIssueMessages(Guid issueId, long? afterId = null)
    {
        using var db = _contextFactory.CreateDbContext();
        var query = db.IssueMessages.AsNoTracking().Where(x => x.IssueId == issueId);
        if (afterId is not null)
            query = query.Where(x => x.Id > afterId.Value);
        // 메시지 번호는 저장 순서대로 커지므로 번호순이 보낸 순서입니다.
        return query.OrderBy(x => x.Id).ToList().Select(ToDto).ToList();
    }

    // ===== 내부 도우미 =====

    // 문제 상태 순서 검사 (안 A). 규칙에 안 맞으면 예외를 던지고, Program.cs가 403/400/409로 바꿔 앱에 알려 줌
    //   작업자: 발생 → 확인 → 조치 중 → 완료 요청 (한 칸씩만)
    //   관리자: 완료 요청 → 종결, 또는 완료 요청 → 조치 중 (반려, 사유 필수)
    //   종결된 문제와 같은 상태로의 변경은 거절
    private static void ValidateIssueTransition(
        IssueEntity issue, UpdateIssueStatusRequest request, UserRole role)
    {
        if (issue.Status == IssueStatus.Closed)
            throw new InvalidOperationException("이미 종결된 문제는 변경할 수 없습니다.");
        if (issue.Status == request.Status)
            throw new InvalidOperationException("현재 상태와 같은 상태로 변경할 수 없습니다.");

        if (role == UserRole.Admin)
        {
            if (request.Status != IssueStatus.Closed && request.Status != IssueStatus.InProgress)
                throw new UnauthorizedAccessException("관리자는 완료 요청을 종결하거나 반려할 수 있습니다.");
            if (issue.Status != IssueStatus.CompletionRequested)
                throw new InvalidOperationException("완료 요청 상태의 문제만 종결하거나 반려할 수 있습니다.");
            if (request.Status == IssueStatus.InProgress && string.IsNullOrWhiteSpace(request.ActionNote))
                throw new ArgumentException("반려 사유를 입력하세요.");
            return;
        }

        if (request.Status == IssueStatus.Closed)
            throw new UnauthorizedAccessException("문제 종결은 관리자만 수행할 수 있습니다.");

        IssueStatus? next = issue.Status switch
        {
            IssueStatus.Occurred => IssueStatus.Acknowledged,
            IssueStatus.Acknowledged => IssueStatus.InProgress,
            IssueStatus.InProgress => IssueStatus.CompletionRequested,
            _ => null
        };

        if (next is null)
            throw new InvalidOperationException("완료 요청 후에는 관리자의 확인을 기다리세요.");
        if (request.Status != next.Value)
            throw new InvalidOperationException("문제 상태는 발생 → 확인 → 조치 중 → 완료 요청 순서로 변경하세요.");
    }

    private static UserEntity? FindUser(AppDbContext db, string id) =>
        // 기존 사용자 ID의 OrdinalIgnoreCase 비교 규칙을 유지합니다.
        db.Users.AsEnumerable().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // 새 사용자 ID 규칙: 영어(반각)·숫자·_·- 만, 3~20자. 전각 'ｔｅｓｔ１'처럼 로그인 때 다시 치기 어려운 ID를 막음
    private static bool IsValidUserId(string id) =>
        id.Length is >= 3 and <= 20 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    // 사용자에게 줄 라인 번호가 모두 실제로 있는 라인인지 확인 (없는 번호면 400)
    private static int[] ValidateLineIds(AppDbContext db, IReadOnlyList<int>? requested)
    {
        if (requested is null) throw new ArgumentException("접근 가능한 라인 목록을 전달하세요.");
        var ids = requested.Distinct().Order().ToArray();
        if (db.Lines.Count(x => ids.Contains(x.Id)) != ids.Length)
            throw new ArgumentException("존재하지 않는 라인이 포함되어 있습니다.");
        return ids;
    }

    private static UserRecord ToRecord(AppDbContext db, UserEntity user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Role = user.Role,
        PasswordHash = user.PasswordHash,
        IsActive = user.IsActive,
        AllowedLineIds = db.UserLineAccesses.AsNoTracking().Where(x => x.UserId == user.Id)
            .OrderBy(x => x.LineId).Select(x => x.LineId).ToList()
    };

    // ToDto: DB 표 모양(Entity)을 앱에 보낼 양식(Dto, Contracts.cs)으로 바꿔 주는 변환 함수들
    public static UserDto ToDto(UserRecord user) =>
        new(user.Id, user.Name, user.Role, user.IsActive, user.AllowedLineIds);

    private static LineStatusDto ToDto(LineEntity line) =>
        new(line.Id, line.Name, line.IsReal, line.OperationStatus, line.InspectionStatus,
            line.NormalCount, line.DefectCount, line.UndeterminedCount, line.UpdatedAt);

    private static InspectionDto ToDto(InspectionEntity inspection) =>
        new(inspection.Id, inspection.LineId, inspection.Result, inspection.Reason, inspection.InspectedAt,
            inspection.DetectionConfidence, inspection.HasImage, inspection.PhoneModel);

    private static IssueDto ToDto(IssueEntity issue) =>
        new(issue.Id, issue.LineId, issue.Type, issue.Title, issue.Description, issue.Status,
            issue.CreatedBy, issue.ActionNote, issue.CreatedAt, issue.UpdatedAt);

    private static IssueMessageDto ToDto(IssueMessageEntity message) =>
        new(message.Id, message.IssueId, message.SenderId, message.SenderName, message.SenderRole,
            message.Message, message.CreatedAt);
}

// 로그인한 사용자 정보를 AuthService(출입증 발급)에 넘길 때 쓰는 모양. 기존 형식을 유지합니다.
public sealed class UserRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public UserRole Role { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; }
    public List<int> AllowedLineIds { get; set; } = [];
}

// 비밀번호 암호화(해시). 원래 비밀번호는 저장하지 않고 "소금(salt).해시값" 문자열만 저장합니다.
// 로그인할 때는 입력한 비밀번호를 같은 방식으로 암호화해서 저장된 값과 비교합니다.
internal static class PasswordHasher
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hash, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
