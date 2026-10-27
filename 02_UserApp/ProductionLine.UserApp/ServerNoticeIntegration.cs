using System.Net;
using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private async Task HandleServerFailureAsync(Exception error, string context, bool refreshIssues = false)
    {
        if (IsDisposed) return;
        // 현재 공용 클라이언트는 HTTP 상태를 보존하지 않고 서버 메시지만 예외로 전달합니다.
        // HttpRequestException의 상태 또는 현재 서버의 명확한 인증 만료 문구만 처리합니다.
        bool unauthorized = error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
                            || error.Message == "로그인이 필요합니다.";
        if (unauthorized)
        {
            SessionExpired = true;
            LogoutRequested = true;
            _refresh.Stop();
            _messageRefresh.Stop();
            StopInspection("로그인이 필요하여 검사를 중지했습니다.");
            Close();
            return;
        }

        // 404/409/권한 변경/불확실한 통신 결과 후 상태를 재조회합니다. 변경 요청은 재전송하지 않습니다.
        if (refreshIssues) await RefreshAfterChangeAsync();
        if (!IsDisposed) _connection.Text = $"{context}: {error.Message}";
    }

    private async Task RefreshAfterChangeAsync()
    {
        // 자동 조회와 겹쳐도 변경 후 조회가 건너뛰어지지 않도록 기다린 뒤 다시 조회합니다.
        while (_loading && !IsDisposed) await Task.Delay(40);
        if (!IsDisposed) await RefreshAsync(false);
    }
}
