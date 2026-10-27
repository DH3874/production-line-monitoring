using _01_AdminApp.Services;
using ProductionLine.ConnectUI;
using ProductionLine.Shared;
namespace _01_AdminApp;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 로그인 전에 "서버 열기 / 서버 접속" 창 (김하균 추가, 2026-10-21)
        using var connect = new ServerConnectForm(allowStartServer: true, appTitle: "관리자");
        AdminTheme.Attach(connect); // 관리자 앱 어두운 테마 (작업자 앱의 같은 창은 그대로)
        if (connect.ShowDialog() != DialogResult.OK || connect.ServerUrl == null) return;
        // 이 앱 안에서 만드는 서버 호출 객체가 모두 고른 주소를 쓰도록 (ProductionLineApiClient가 이 값을 읽음)
        Environment.SetEnvironmentVariable("PRODUCTIONLINE_SERVER_URL", connect.ServerUrl);
        using (var api = new ProductionLineApiClient(connect.ServerUrl))
        {
            Application.Run(new AdminLoginForm(new ApiAdminAuthService(api),
                new ApiDashboardService(api), new ApiLineDetailService(api), new ApiUserService(api), api));
        }
        connect.OfferToStopStartedServer();
    }
}
