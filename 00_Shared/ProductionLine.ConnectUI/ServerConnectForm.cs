using System.Diagnostics;
using ProductionLine.Shared;

namespace ProductionLine.ConnectUI;

// [서버 연결 창] 앱을 켜면 로그인 전에 먼저 뜨는 창 (김하균 추가, 2026-10-21)
// - 관리자 앱: "서버 열기"만 (이 PC에서 서버를 켜고 접속. 이미 켜져 있으면 그 서버에 접속)
// - 작업자 앱: "서버 접속"만 (같은 네트워크의 서버를 자동으로 찾거나 IP 직접 입력)
// 두 앱 csproj에 링크로 들어감 (00_Shared/ProductionLine.ConnectUI)
public sealed class ServerConnectForm : Form
{
    private static readonly string LastServerFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProductionLine", "last-server.txt");

    private readonly bool _allowStart;
    private readonly Label _thisPc = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(0, 105, 92), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _openButton = new() { Text = "서버 켜고 접속", Dock = DockStyle.Fill };
    private readonly ListBox _servers = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("맑은 고딕", 10.5f) };
    private readonly Button _refresh = new() { Text = "다시 찾기", Dock = DockStyle.Fill };
    private readonly Button _connectSelected = new() { Text = "접속", Dock = DockStyle.Fill };
    private readonly TextBox _manual = new() { Dock = DockStyle.Fill, PlaceholderText = "예: 192.168.0.15", Font = new Font("맑은 고딕", 10.5f) };
    private readonly Button _connectManual = new() { Text = "접속", Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _close = new() { Text = "닫기", Dock = DockStyle.Fill };
    private CancellationTokenSource? _search;
    private bool _busy;

    // 고른 서버 주소 (예: http://192.168.0.15:5050). 닫기를 누르면 null
    public string? ServerUrl { get; private set; }
    // "서버 열기"로 이 앱이 켠 서버 프로그램 (앱을 끌 때 같이 끌지 물어봄)
    public Process? StartedServer { get; private set; }

    public ServerConnectForm(bool allowStartServer, string appTitle)
    {
        _allowStart = allowStartServer;
        Text = $"서버 연결 · {appTitle}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("맑은 고딕", 10);
        BackColor = Color.White;
        ClientSize = new Size(600, allowStartServer ? 340 : 450);
        Padding = new Padding(22, 18, 22, 16);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        void Row(Control control, SizeType type, float size)
        {
            layout.RowStyles.Add(new RowStyle(type, size));
            layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
        }

        Row(new Label { Text = allowStartServer ? "서버 열기" : "서버 연결", AutoSize = true, Font = new Font("맑은 고딕", 16, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) }, SizeType.AutoSize, 0);
        Row(new Label
        {
            Text = allowStartServer
                ? "이 PC에서 서버를 켜고 관리자로 접속합니다. 작업자 PC는 아래 주소로 접속하세요."
                : "같은 네트워크(공유기)에서 켜진 서버를 찾아 접속합니다.",
            AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 12)
        }, SizeType.AutoSize, 0);

        // 관리자 앱: "서버 열기"만 (10-21 김하균 결정 — 관리자는 다른 서버에 접속할 일이 없음)
        if (allowStartServer)
        {
            _thisPc.Font = new Font("맑은 고딕", 13, FontStyle.Bold);
            _thisPc.TextAlign = ContentAlignment.MiddleCenter;
            _thisPc.BackColor = Color.FromArgb(235, 246, 244);
            _thisPc.Margin = new Padding(0, 0, 0, 10);
            Row(_thisPc, SizeType.Absolute, 70);
            _openButton.Font = new Font("맑은 고딕", 12, FontStyle.Bold);
            _openButton.Margin = new Padding(0, 0, 0, 6);
            Row(_openButton, SizeType.Absolute, 54);
            Row(_status, SizeType.Percent, 100);
            var closeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            closeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            closeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            closeRow.Controls.Add(_close, 1, 0);
            Row(closeRow, SizeType.Absolute, 40);
            Controls.Add(layout);
            StyleButtons();
            _openButton.Click += async (_, _) => await OpenServerAsync();
            _close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Shown += (_, _) =>
            {
                var best = LocalNetwork.GetBestLanIPv4();
                _thisPc.Text = best == null
                    ? "이 PC 네트워크 주소 없음 (이 PC에서만 접속 가능)"
                    : $"이 PC 주소   {best}:{ServerDiscovery.DefaultHttpPort}";
                UpdateModes();
                Status("[서버 켜고 접속]을 누르면 이 PC에서 서버가 켜지고 관리자 로그인 창이 열려요.\r\n이미 서버가 켜져 있으면 그 서버에 바로 접속해요.");
            };
            return;
        }

        Row(new Label { Text = "찾은 서버", AutoSize = true, Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold) }, SizeType.AutoSize, 0);

        var found = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 6) };
        found.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        found.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        found.RowCount = 3;
        found.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        found.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        found.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        found.Controls.Add(_servers, 0, 0); found.SetRowSpan(_servers, 3);
        found.Controls.Add(_connectSelected, 1, 0);
        found.Controls.Add(_refresh, 1, 1);
        Row(found, SizeType.Percent, 100);

        var manual = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 6) };
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        manual.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        manual.Controls.Add(new Label { Text = "직접 입력", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        manual.Controls.Add(_manual, 1, 0);
        manual.Controls.Add(_connectManual, 2, 0);
        Row(manual, SizeType.Absolute, 40);

        Row(_status, SizeType.Absolute, 52);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bottom.Controls.Add(_close, 1, 0);
        Row(bottom, SizeType.Absolute, 40);
        Controls.Add(layout);
        StyleButtons();

        // 작업자 앱: 서버 찾기 목록 + 직접 입력
        _refresh.Click += async (_, _) => await SearchAsync();
        _connectSelected.Click += async (_, _) => await ConnectSelectedAsync();
        _servers.DoubleClick += async (_, _) => await ConnectSelectedAsync();
        _connectManual.Click += async (_, _) => await ConnectManualAsync();
        _manual.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ConnectManualAsync(); } };
        _close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        FormClosing += (_, _) => _search?.Cancel();
        Shown += async (_, _) =>
        {
            _manual.Text = LoadLastServer();
            UpdateModes();
            await SearchAsync();
        };
    }

    private void StyleButtons()
    {
        foreach (var button in new[] { _openButton, _connectSelected, _connectManual })
        {
            button.BackColor = Color.FromArgb(0, 119, 117);
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            if (button != _openButton) button.Font = new Font("맑은 고딕", 10, FontStyle.Bold);
        }
    }

    private void UpdateModes()
    {
        var open = _allowStart; // 관리자 앱 = 서버 열기 전용, 작업자 앱 = 서버 접속 전용
        _thisPc.Enabled = _openButton.Enabled = open && !_busy;
        foreach (var control in new Control[] { _servers, _refresh, _connectSelected, _manual, _connectManual })
            control.Enabled = !open && !_busy;
        // 꺼진 버튼은 회색으로 (색을 직접 칠한 버튼은 꺼져도 색이 그대로라 눌리는 것처럼 보임)
        foreach (var button in new[] { _openButton, _connectSelected, _connectManual })
        {
            button.BackColor = button.Enabled ? Color.FromArgb(0, 119, 117) : Color.FromArgb(214, 220, 228);
            button.ForeColor = button.Enabled ? Color.White : Color.FromArgb(120, 130, 145);
        }
    }

    private void SetBusy(bool busy) { _busy = busy; UpdateModes(); _close.Enabled = !busy; }

    private void Status(string text, bool error = false)
    {
        _status.ForeColor = error ? Color.Firebrick : Color.FromArgb(40, 50, 65);
        _status.Text = text;
    }

    private async Task SearchAsync()
    {
        if (_busy || IsDisposed) return;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        _refresh.Enabled = false;
        Status("같은 네트워크에서 서버를 찾는 중...");
        try
        {
            var servers = await ServerDiscovery.FindAsync(TimeSpan.FromSeconds(1.5), token);
            if (IsDisposed || token.IsCancellationRequested) return;
            _servers.Items.Clear();
            foreach (var server in servers) _servers.Items.Add(server);
            if (servers.Count > 0)
            {
                _servers.SelectedIndex = 0;
                Status($"서버 {servers.Count}개를 찾았어요. 고르고 [접속]을 누르세요.");
            }
            else
            {
                Status(_allowStart
                    ? "켜진 서버를 못 찾았어요. 이 PC에서 서버를 켜려면 위의 '서버 열기'를 고르세요. (다른 PC 서버면 IP를 직접 입력)"
                    : "서버를 못 찾았어요. 서버 PC가 켜져 있는지, 같은 공유기인지 확인하거나 아래에 IP를 직접 입력하세요.", true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) Status("서버 찾기 실패: " + ex.Message, true); }
        finally { if (!IsDisposed) UpdateModes(); }
    }

    private async Task ConnectSelectedAsync()
    {
        if (_servers.SelectedItem is not DiscoveredServer server) { Status("찾은 서버 목록에서 서버를 고르세요.", true); return; }
        await ConnectAsync(server.Url);
    }

    private async Task ConnectManualAsync()
    {
        if (!ServerDiscovery.TryNormalizeAddress(_manual.Text, out var url))
        {
            Status("IP 주소를 확인하세요. 예: 192.168.0.15 또는 192.168.0.15:5050", true);
            _manual.Focus();
            return;
        }
        await ConnectAsync(url);
    }

    private async Task ConnectAsync(string url)
    {
        if (_busy) return;
        SetBusy(true);
        Status($"{url} 연결 확인 중...");
        try
        {
            if (!await ServerLauncher.IsServerAliveAsync(url, TimeSpan.FromSeconds(3)))
            {
                Status($"{url} 에서 서버가 응답하지 않아요. 서버가 켜져 있는지, 주소와 방화벽(TCP {ServerDiscovery.DefaultHttpPort})을 확인하세요.", true);
                return;
            }
            Finish(url);
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task OpenServerAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var local = $"http://127.0.0.1:{ServerDiscovery.DefaultHttpPort}";
            if (await ServerLauncher.IsServerAliveAsync(local, TimeSpan.FromSeconds(1.5)))
            {
                Status("이 PC에서 이미 서버가 켜져 있어요. 그 서버에 접속합니다.");
                Finish(await ThisPcUrlAsync(local));
                return;
            }

            var exe = ServerLauncher.FindServerExe();
            if (exe == null)
            {
                using var dialog = new OpenFileDialog { Title = "서버 프로그램(ProductionLine.Server.exe)을 고르세요", Filter = "생산라인 서버|ProductionLine.Server.exe" };
                if (dialog.ShowDialog(this) != DialogResult.OK) { Status("서버 프로그램을 찾지 못했어요. 앱 폴더의 Server 폴더에 서버 프로그램이 있어야 합니다.", true); return; }
                exe = dialog.FileName;
            }

            Status("서버를 켜는 중... Windows 방화벽 창이 뜨면 '허용'을 누르세요.");
            StartedServer = ServerLauncher.Start(exe);
            for (var i = 0; i < 80; i++) // 최대 40초 기다림
            {
                if (StartedServer.HasExited)
                {
                    Status("서버가 켜지자마자 꺼졌어요. 다른 서버가 이미 켜져 있거나(포트 5050 사용 중) 설정 문제일 수 있어요.", true);
                    StartedServer = null;
                    return;
                }
                if (await ServerLauncher.IsServerAliveAsync(local, TimeSpan.FromSeconds(1))) { Finish(await ThisPcUrlAsync(local)); return; }
                await Task.Delay(500);
            }
            Status("서버가 40초 안에 응답하지 않았어요. 서버 창의 메시지를 확인하세요.", true);
        }
        catch (Exception ex) { Status("서버를 켜지 못했어요: " + ex.Message, true); }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    // 이 PC 서버는 화면에 IPv4로 보이게 (예: http://192.168.0.15:5050). 그 주소로 응답이 없으면 127.0.0.1 사용
    private static async Task<string> ThisPcUrlAsync(string localUrl)
    {
        var best = LocalNetwork.GetBestLanIPv4();
        if (best == null) return localUrl;
        var lan = $"http://{best}:{ServerDiscovery.DefaultHttpPort}";
        return await ServerLauncher.IsServerAliveAsync(lan, TimeSpan.FromSeconds(2)) ? lan : localUrl;
    }

    private void Finish(string url)
    {
        ServerUrl = url;
        SaveLastServer(url);
        DialogResult = DialogResult.OK;
        Close();
    }

    // 앱을 끌 때: 이 앱이 켠 서버가 아직 켜져 있으면 같이 끌지 물어봄
    public void OfferToStopStartedServer()
    {
        if (StartedServer is not { HasExited: false } server) return;
        var answer = MessageBox.Show(
            "이 PC에서 켠 서버도 끌까요?\r\n\r\n끄면 다른 PC(작업자 앱)의 연결도 끊어집니다.\r\n'아니요'를 누르면 서버 창은 계속 켜져 있습니다.",
            "서버 끄기", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try { server.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }

    private static string LoadLastServer()
    {
        try
        {
            if (File.Exists(LastServerFile)) return new Uri(File.ReadAllText(LastServerFile).Trim()).Authority;
            var env = Environment.GetEnvironmentVariable("PRODUCTIONLINE_SERVER_URL");
            return string.IsNullOrWhiteSpace(env) ? "" : new Uri(env).Authority;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException) { return ""; }
    }

    private static void SaveLastServer(string url)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LastServerFile)!); File.WriteAllText(LastServerFile, url); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _search?.Dispose();
        base.Dispose(disposing);
    }
}
