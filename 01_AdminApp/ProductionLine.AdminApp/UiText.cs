using ProductionLine.Shared;

namespace _01_AdminApp;

// Shared labels used by completion review, independent of the removed inquiry screen.
internal static class UiText
{
    public static string Status(IssueStatus status) => status switch
    {
        IssueStatus.Occurred => "발생",
        IssueStatus.Acknowledged => "확인",
        IssueStatus.InProgress => "조치 중",
        IssueStatus.CompletionRequested => "완료 요청",
        IssueStatus.Closed => "종결",
        _ => status.ToString()
    };
}
