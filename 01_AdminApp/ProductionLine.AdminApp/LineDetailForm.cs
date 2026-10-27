using _01_AdminApp.Services;
using ProductionLine.Shared;
namespace _01_AdminApp;

public sealed class LineDetailForm : Form
{
    private readonly int _lineId;
    private readonly IDashboardService _dashboard;
    private readonly ILineDetailService _detail;
    private readonly LineStatusCard _card = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 50 };
    private bool _busy;
    public LineDetailForm(int lineId, IDashboardService dashboard, ILineDetailService detail, ProductionLineApiClient? api = null)
    {
        AdminTheme.AttachOnLoad(this);
        _lineId = lineId; _dashboard = dashboard; _detail = detail;
        Text = $"LINE {lineId} · 대표 라인 상세";
        ClientSize = new Size(1040, 850); MinimumSize = new Size(850, 700);
        Font = new Font("맑은 고딕", 10); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.Controls.Add(_card, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        tools.Controls.Add(new Label { Text = "최근 검사 이력 (최근 100건)", AutoSize = true });
        if (api != null)
        {
            var remote = new Button { Text = "작업자 원격 제어 · 센서 그래프", AutoSize = true };
            remote.Click += (_, _) => { using var form = new RemoteWorkerForm(api, _lineId); form.ShowDialog(this); };
            tools.Controls.Add(remote);
        }
        layout.Controls.Add(tools, 0, 1);
        foreach (var column in new[] { ("Result", "판정"), ("Reason", "사유"), ("InspectedAt", "검사 시각") })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = column.Item1, HeaderText = column.Item2 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.Value is InspectionResultType result) { e.Value = result switch { InspectionResultType.Normal => "정상", InspectionResultType.Defect => "불량", _ => "판정 불가" }; e.FormattingApplied = true; }
        };
        layout.Controls.Add(_grid, 0, 2); layout.Controls.Add(_status, 0, 3); Controls.Add(layout);
        _animation.Tick += (_, _) => { if (Enabled && WindowState != FormWindowState.Minimized) _card.Animate(); };
        _refresh.Tick += async (_, _) => await ReloadAsync();
        Shown += async (_, _) => { await ReloadAsync(); if (!IsDisposed) { _refresh.Start(); _animation.Start(); } };
        FormClosed += (_, _) => { _refresh.Dispose(); _animation.Dispose(); };
    }
    private async Task ReloadAsync()
    {
        if (_busy || IsDisposed || Disposing) return;
        _busy = true;
        try
        {
            var lines = await _dashboard.GetLinesAsync();
            var issues = await _dashboard.GetIssuesAsync();
            var inspections = await _detail.GetInspectionsAsync(_lineId);
            if (IsDisposed || Disposing) return;
            var line = lines.FirstOrDefault(l => l.Id == _lineId) ?? throw new InvalidOperationException("라인을 찾을 수 없습니다.");
            _card.UpdateLine(line, issues.Count(i => i.LineId == _lineId && i.Status != IssueStatus.Closed), issues.Count(i => i.LineId == _lineId && i.Status == IssueStatus.CompletionRequested));
            _grid.DataSource = inspections.OrderByDescending(i => i.InspectedAt).ToList();
            _status.Text = $"1초마다 갱신 · {DateTime.Now:HH:mm:ss} · 애니메이션은 실제 제품 위치와 무관한 시연입니다.";
        }
        catch (Exception ex) { if (!IsDisposed && !Disposing) _status.Text = ex.Message; }
        finally { _busy = false; }
    }
}
