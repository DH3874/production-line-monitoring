using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed class DeviceDiagnosticsForm : Form
{
    private readonly ProductionLineApiClient _api;
    private readonly int _lineId;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Button _refresh = new() { Text = "다시 확인", Dock = DockStyle.Fill };
    private readonly Label _status = new() { Text = "서버에서 장비 보고를 확인하고 있습니다.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly DataGridView _devices = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        RowHeadersVisible = false, AutoGenerateColumns = false, BackgroundColor = IndustrialPalette.Surface,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly TextBox _appStatus = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.None, BackColor = Color.FromArgb(13, 23, 34), ForeColor = IndustrialPalette.Text };
    private bool _checking, _resourcesDisposed;
    public DeviceDiagnosticsSnapshot? LastSnapshot { get; private set; }

    public DeviceDiagnosticsForm(ProductionLineApiClient api, int lineId)
    {
        _api = api; _lineId = lineId;
        Text = $"LINE {lineId} · 장비 상태 확인";
        ClientSize = new Size(1080, 670); MinimumSize = new Size(820, 540);
        Font = new Font("Noto Sans KR", 10); BackColor = IndustrialPalette.Canvas; ForeColor = IndustrialPalette.Text;
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14), BackColor = IndustrialPalette.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "기존 작업자 앱·보드가 보내는 상태를 2초마다 확인합니다. AI 상담에도 같은 보고를 사용합니다.\r\n핀 표시는 예상 배선입니다. 센서 장착·실제 점등·회전은 별도 확인이 필요합니다.", AutoEllipsis = true }, 0, 0);
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        toolbar.Controls.Add(_status, 0, 0); toolbar.Controls.Add(_refresh, 1, 0);
        layout.Controls.Add(toolbar, 0, 1);
        AddColumn("장비", 130); AddColumn("연결 핀 / USB", 150); AddColumn("확인 내용", 0); AddColumn("마지막 보고", 100);
        _devices.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _devices.DefaultCellStyle.Padding = new Padding(4);
        _devices.EnableHeadersVisualStyles = false; _devices.BorderStyle = BorderStyle.None; _devices.GridColor = IndustrialPalette.Border;
        _devices.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = IndustrialPalette.SurfaceRaised, ForeColor = IndustrialPalette.Text, Font = new Font("Noto Sans KR", 9, FontStyle.Bold) };
        _devices.DefaultCellStyle.BackColor = IndustrialPalette.Surface; _devices.DefaultCellStyle.ForeColor = IndustrialPalette.Text;
        _devices.DefaultCellStyle.SelectionBackColor = Color.FromArgb(24, 75, 82); _devices.DefaultCellStyle.SelectionForeColor = Color.White;
        _devices.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        layout.Controls.Add(_devices, 0, 2);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "작업자 앱의 최근 오류·검사 기록 (상태 보고이며 AI 명령이 아닙니다.)", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        layout.Controls.Add(_appStatus, 0, 4); Controls.Add(layout);
        _refresh.FlatStyle = FlatStyle.Flat; _refresh.FlatAppearance.BorderColor = IndustrialPalette.Border;
        _refresh.BackColor = IndustrialPalette.SurfaceRaised; _refresh.ForeColor = IndustrialPalette.Text;
        _refresh.Click += async (_, _) => await RefreshDiagnosticsAsync();
        _timer.Tick += async (_, _) => await RefreshDiagnosticsAsync();
        Shown += async (_, _) => { _timer.Start(); await RefreshDiagnosticsAsync(); };
    }
    private void AddColumn(string title, int width)
    {
        _devices.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, Width = Math.Max(50, width),
            AutoSizeMode = width == 0 ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = width == 0 ? 200 : 60, SortMode = DataGridViewColumnSortMode.NotSortable });
    }
    internal async Task RefreshDiagnosticsAsync()
    {
        if (_checking || IsDisposed || Disposing) return;
        _checking = true; _refresh.Enabled = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var snapshot = await AiConsultationApi.GetDiagnosticsAsync(_api, _lineId, timeout.Token);
            if (IsDisposed || Disposing) return;
            var firstRow = _devices.FirstDisplayedScrollingRowIndex;
            LastSnapshot = snapshot; _devices.Rows.Clear();
            var now = DateTimeOffset.UtcNow;
            foreach (var device in snapshot.Devices)
            {
                var row = _devices.Rows[_devices.Rows.Add(device.Name, device.Connection,
                    DeviceDiagnosticsText.Describe(device, now), device.ObservedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "미수신")];
                row.DefaultCellStyle.ForeColor = device.State is "alert" or "read_error" ? Color.Firebrick :
                    device.IsCurrent(now) && device.State is "received" or "reading" ? Color.SeaGreen : Color.DimGray;
            }
            _devices.ClearSelection();
            if (firstRow >= 0 && firstRow < _devices.Rows.Count) _devices.FirstDisplayedScrollingRowIndex = firstRow;
            _status.Text = $"서버 보고 조회: {snapshot.CapturedAt.ToLocalTime():HH:mm:ss} · 수신 시각과 확인 내용을 함께 보세요.";
            _status.ForeColor = Color.FromArgb(23, 39, 58);
            _appStatus.Text = string.IsNullOrWhiteSpace(snapshot.AppStatus) ? "작업자 앱의 최근 상태 기록 없음" : snapshot.AppStatus;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            // A failed refresh must not leave old green/normal rows on screen.
            LastSnapshot = null; _devices.Rows.Clear(); _appStatus.Clear();
            _status.Text = ex is OperationCanceledException ? "서버 조회 시간 초과 · 현재 장비 상태 미확인" : "장비 상태 조회 실패 · " + ex.Message;
            _status.ForeColor = Color.Firebrick;
        }
        finally { _checking = false; if (!IsDisposed && !Disposing) _refresh.Enabled = true; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _timer.Dispose(); _lifetime.Cancel(); _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
