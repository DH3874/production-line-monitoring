using System.Net.Http;
using System.Text.Json;

namespace _01_AdminApp;

// Read-only public health endpoint. Does not log in or change the shared token.
public sealed class ServerConnectionControl : UserControl
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly Label _state = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _session = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
    private readonly Button _check = new() { Text = "연결 확인", Dock = DockStyle.Fill };
    private bool _checking;
    public string ConnectionText => _state.Text;

    public ServerConnectionControl(Uri? address)
    {
        Height = 100;
        _http.BaseAddress = address;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var url = new Label { Text = "서버: " + (address?.ToString() ?? "설정 없음"), Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(url, 0, 0); layout.SetColumnSpan(url, 2);
        layout.Controls.Add(_state, 0, 1); layout.Controls.Add(_check, 1, 1);
        layout.Controls.Add(_session, 0, 2); layout.SetColumnSpan(_session, 2);
        Controls.Add(layout);
        _state.Text = "서버 연결 확인 전";
        _session.Text = "로그인 전 · 서버 응답 확인과 계정 인증은 별개입니다.";
        _check.Click += async (_, _) => await CheckAsync();
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Visible && !IsDisposed && !Disposing) { _timer.Start(); await CheckAsync(); }
    }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (IsDisposed || Disposing) return;
        _timer.Enabled = Visible;
        if (Visible) await CheckAsync();
    }

    public void SetSessionStatus(string text) { if (!IsDisposed) _session.Text = text; }

    public async Task CheckAsync()
    {
        if (_checking || IsDisposed || Disposing) return;
        _checking = true; _check.Enabled = false;
        try
        {
            if (_http.BaseAddress == null) { Display(false, "서버 주소 설정 없음"); return; }
            _state.Text = "서버 연결 확인 중...";
            using var response = await _http.GetAsync("");
            if (IsDisposed || Disposing) return;
            if (!response.IsSuccessStatusCode) { Display(false, $"서버 확인 실패 · HTTP {(int)response.StatusCode}"); return; }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            bool valid = json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("service", out var name) &&
                name.ValueKind == JsonValueKind.String && name.GetString() == "ProductionLine.Server";
            Display(valid, valid ? "서버 연결 정상" : "다른 서비스가 응답함 · 주소 확인 필요");
        }
        catch (TaskCanceledException) { Display(false, "서버 응답 시간 초과"); }
        catch (HttpRequestException) { Display(false, "서버 연결 안 됨 · 실행 상태/주소 확인"); }
        catch (JsonException) { Display(false, "서버 응답 형식 불일치 · 주소 확인"); }
        catch (Exception) { Display(false, "서버 상태 확인 실패"); }
        finally { _checking = false; if (!IsDisposed && !Disposing) _check.Enabled = true; }
    }
    private void Display(bool connected, string text)
    {
        if (IsDisposed || Disposing) return;
        _state.ForeColor = connected ? Color.SeaGreen : Color.Firebrick;
        _state.Text = $"{text} · {DateTime.Now:HH:mm:ss}";
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _http.Dispose(); }
        base.Dispose(disposing);
    }
}
