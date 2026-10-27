namespace ProductionLine.Shared;

public enum UserRole { Admin, User }
public enum InspectionResultType { Normal, Defect, Undetermined }
public enum IssueType { ProductionLine, InspectionSystem }
public enum IssueStatus { Occurred, Acknowledged, InProgress, CompletionRequested, Closed }

public sealed record LoginRequest(string UserId, string Password);
public sealed record LoginResponse(string Token, UserDto User);

public sealed record UserDto(
    string Id,
    string Name,
    UserRole Role,
    bool IsActive,
    IReadOnlyList<int> AllowedLineIds);

public sealed record CreateUserRequest(
    string Id,
    string Name,
    string Password,
    IReadOnlyList<int> AllowedLineIds);

public sealed record UpdateUserRequest(
    string Name,
    bool IsActive,
    IReadOnlyList<int> AllowedLineIds,
    string? NewPassword = null);

public sealed record LineStatusDto(
    int Id,
    string Name,
    bool IsReal,
    string OperationStatus,
    string InspectionStatus,
    int NormalCount,
    int DefectCount,
    int UndeterminedCount,
    DateTimeOffset UpdatedAt,
    LineHardwareStatusDto? Hardware = null);

public sealed record InspectionRequest(
    int LineId,
    InspectionResultType Result,
    string Reason,
    bool ImageAcquired = true);

public sealed record CameraInspectionRequest(
    int LineId,
    InspectionResultType Result,
    string Reason,
    double Confidence,
    bool ClassificationAvailable,
    byte[]? ResultImage = null,
    // 2026-10-22 추가 (김하균): YOLO가 알아낸 휴대폰 기종(예: "iPhone 13", 없으면 null)과
    // 제품 1대마다 앱이 만드는 요청 번호. 같은 번호가 다시 오면 서버는 새로 저장하지 않고 처음 기록을 돌려줌
    string? PhoneModel = null,
    Guid? RequestId = null);

public sealed record InspectionDto(
    Guid Id,
    int LineId,
    InspectionResultType Result,
    string Reason,
    DateTimeOffset InspectedAt,
    double? DetectionConfidence = null,
    bool HasImage = false,
    string? PhoneModel = null);

public sealed record CreateIssueRequest(int LineId, IssueType Type, string Title, string Description);

public sealed record SendIssueMessageRequest(string Message);
public sealed record CreateIssueMessageRequest(string Message);
public sealed record IssueMessageDto(long Id, Guid IssueId, string SenderId, string SenderName,
    UserRole SenderRole, string Message, DateTimeOffset CreatedAt);

public sealed record IssueDto(
    Guid Id,
    int LineId,
    IssueType Type,
    string Title,
    string Description,
    IssueStatus Status,
    string CreatedBy,
    string? ActionNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateIssueStatusRequest(IssueStatus Status, string ActionNote);
public sealed record AiConsultRequest(Guid? IssueId, string Question);
public sealed record AiConsultResponse(string Answer, string ManualSource, bool NeedAdmin);
public sealed record ApiMessage(string Message);
