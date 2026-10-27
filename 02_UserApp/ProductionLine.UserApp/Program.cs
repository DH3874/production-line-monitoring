using ProductionLine.ConnectUI;

namespace ProductionLine.UserApp;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        // 로그인 전에 "서버 접속" 창 — 같은 네트워크의 서버를 찾거나 IP 직접 입력 (김하균 추가, 2026-10-21)
        using (var connect = new ServerConnectForm(allowStartServer: false, appTitle: "작업자"))
        {
            ConnectWindowTheme.Attach(connect); // 관리자 앱 서버 열기 창과 같은 어두운 테마
            if (connect.ShowDialog() != DialogResult.OK || connect.ServerUrl == null) return;
            // 로그인 화면의 new ProductionLineApiClient()가 이 주소를 읽어서 그 서버에 연결함
            Environment.SetEnvironmentVariable("PRODUCTIONLINE_SERVER_URL", connect.ServerUrl);
        }
        Application.Run(new UserLoginForm());
    }
}
