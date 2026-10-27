using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProductionLine.Shared;

namespace _01_AdminApp.Services
{
    public interface IDashboardService
    {
        Task<List<LineStatusDto>> GetLinesAsync();
        Task<List<IssueDto>> GetIssuesAsync();
        Task<IssueDto> UpdateIssueAsync(Guid id, UpdateIssueStatusRequest request);
    }

    internal static class IssueReviewRules
    {
        public static void Validate(IssueDto issue, UpdateIssueStatusRequest request)
        {
            if (issue.Status != IssueStatus.CompletionRequested)
                throw new InvalidOperationException("완료 요청 상태가 아닙니다. 목록을 새로고침해 주세요.");
            if (request.Status != IssueStatus.Closed && request.Status != IssueStatus.InProgress)
                throw new InvalidOperationException("종결 또는 반려만 처리할 수 있습니다.");
            if (string.IsNullOrWhiteSpace(request.ActionNote))
                throw new InvalidOperationException("처리 사유를 입력하세요.");
        }

        public static UpdateIssueStatusRequest WithPreviousNote(
            IssueDto issue, UpdateIssueStatusRequest request)
        {
            Validate(issue, request);
            string action = request.Status == IssueStatus.Closed ? "관리자 종결" : "관리자 반려 · 재조치 요청";
            string note = $"[{action} / {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}]\r\n{request.ActionNote.Trim()}";
            if (!string.IsNullOrWhiteSpace(issue.ActionNote))
                note = issue.ActionNote + "\r\n\r\n" + note;
            return new UpdateIssueStatusRequest(request.Status, note);
        }
    }

    // 하나의 서비스 객체를 모든 화면이 공유하여 변경된 문제 상태 유지
    public sealed class MockDashboardService : IDashboardService
    {
        private readonly DateTimeOffset _createdAt = DateTimeOffset.Now;
        private readonly List<IssueDto> _issues = new List<IssueDto>
        {
            new IssueDto(Guid.NewGuid(), 1, IssueType.InspectionSystem,
                "카메라 연결 점검", "영상 연결이 일시적으로 끊겼습니다.",
                IssueStatus.CompletionRequested, "worker1", "케이블 재연결 후 정상 동작 확인",
                DateTimeOffset.Now.AddMinutes(-30), DateTimeOffset.Now.AddMinutes(-5)),
            new IssueDto(Guid.NewGuid(), 3, IssueType.ProductionLine,
                "컨베이어 정지", "설비 상태 점검이 필요합니다.",
                IssueStatus.InProgress, "worker2", "원인 확인 중",
                DateTimeOffset.Now.AddMinutes(-20), DateTimeOffset.Now.AddMinutes(-10))
        };

        public Task<List<LineStatusDto>> GetLinesAsync() => Task.FromResult(new List<LineStatusDto>
        {
            new LineStatusDto(1, "LINE 1 · 대표 라인", true, "가동", "검사 중", 120, 5, 2, _createdAt),
            new LineStatusDto(2, "LINE 2 · 가상", false, "가동", "가상 데이터", 96, 2, 0, _createdAt),
            new LineStatusDto(3, "LINE 3 · 가상", false, "공정 정지", "가상 데이터", 81, 3, 0, _createdAt),
            new LineStatusDto(4, "LINE 4 · 가상", false, "가동", "가상 데이터", 103, 1, 2, _createdAt)
        });

        public Task<List<IssueDto>> GetIssuesAsync() => Task.FromResult(new List<IssueDto>(_issues));

        public Task<IssueDto> UpdateIssueAsync(Guid id, UpdateIssueStatusRequest request)
        {
            int index = _issues.FindIndex(x => x.Id == id);
            if (index < 0) throw new InvalidOperationException("문제를 찾을 수 없습니다.");
            var combined = IssueReviewRules.WithPreviousNote(_issues[index], request);
            var updated = _issues[index] with
            {
                Status = combined.Status,
                ActionNote = combined.ActionNote,
                UpdatedAt = DateTimeOffset.Now
            };
            _issues[index] = updated;
            return Task.FromResult(updated);
        }
    }

    public sealed class ApiDashboardService : IDashboardService
    {
        private readonly ProductionLineApiClient _api;
        public ApiDashboardService(ProductionLineApiClient api) => _api = api;
        public Task<List<LineStatusDto>> GetLinesAsync() => ServerCall.Run(() => _api.GetLinesAsync());
        public Task<List<IssueDto>> GetIssuesAsync() => ServerCall.Run(() => _api.GetIssuesAsync());

        public async Task<IssueDto> UpdateIssueAsync(Guid id, UpdateIssueStatusRequest request)
        {
            if (_api.CurrentUser is not { Role: UserRole.Admin })
                throw new InvalidOperationException("관리자 로그인이 필요합니다.");
            // 처리 직전 재조회. 동시 수정의 최종 보장은 서버에서 처리해야 함.
            var issues = await ServerCall.Run(() => _api.GetIssuesAsync());
            var current = issues.FirstOrDefault(x => x.Id == id)
                ?? throw new InvalidOperationException("문제를 찾을 수 없습니다.");
            var combined = IssueReviewRules.WithPreviousNote(current, request);
            // 기존 공용 함수와 DTO를 그대로 사용
            return await ServerCall.Run(() => _api.UpdateIssueAsync(id, combined));
        }
    }
}
