using ProductionLine.Shared;

namespace _01_AdminApp;

internal sealed class IssueConversationPanel : UserControl
{
    private readonly ProductionLineApiClient? _api;
    private readonly Label _state = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly RichTextBox _history = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
    private readonly TextBox _draft = new() { Dock = DockStyle.Fill, Multiline = true, MaxLength = 2000 };
    private readonly Button _send = new() { Text = "전송", Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly Dictionary<Guid, string> _drafts = new();
    private readonly Dictionary<Guid, SortedDictionary<long, IssueMessageDto>> _messages = new();
    private IssueDto? _issue;
    private bool _polling, _sending;

    public IssueConversationPanel(ProductionLineApiClient? api)
    {
        _api = api; Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.Controls.Add(_state, 0, 0); layout.Controls.Add(_history, 0, 1);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        composer.Controls.Add(_draft, 0, 0); composer.Controls.Add(_send, 1, 0);
        layout.Controls.Add(composer, 0, 2); Controls.Add(layout);
        _send.Click += async (_, _) => await SendAsync();
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start(); UpdateAvailability();
    }

    public void SetIssue(IssueDto? issue)
    {
        if (_issue?.Id != issue?.Id)
        {
            if (_issue != null) _drafts[_issue.Id] = _draft.Text;
            _draft.Text = issue != null && _drafts.TryGetValue(issue.Id, out var text) ? text : "";
            _issue = issue; Render();
            _ = PollAsync();
        }
        _issue = issue; UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        var allowed = _api != null && _issue?.Status == IssueStatus.InProgress && !_sending;
        _send.Enabled = allowed; _draft.ReadOnly = !allowed;
        _state.Text = _api == null ? "서버 연결 후 채팅할 수 있습니다." : _issue == null ? "채팅할 문제를 선택하세요." :
            "작업자와 채팅 · " + (_issue.Status == IssueStatus.InProgress ? "조치 중 · 전송 가능" : "읽기 전용 · 조치 중에만 전송 가능");
    }

    private async Task PollAsync()
    {
        if (_polling || _api == null || _issue == null || IsDisposed) return;
        _polling = true; var id = _issue.Id;
        try
        {
            var cursor = _messages.TryGetValue(id, out var cached) && cached.Count > 0 ? (long?)cached.Keys.Max() : null;
            var messages = await _api.GetIssueMessagesAsync(id, cursor);
            if (IsDisposed) return;
            if (!_messages.TryGetValue(id, out var data)) _messages[id] = data = new();
            foreach (var message in messages.Where(m => m.IssueId == id)) data[message.Id] = message;
            if (_issue?.Id == id) Render();
        }
        catch (Exception ex) { if (!IsDisposed && _issue?.Id == id) _state.Text = "대화 조회 실패: " + ex.Message; }
        finally { _polling = false; }
    }

    private async Task SendAsync()
    {
        if (_sending || _api == null || _issue is not { Status: IssueStatus.InProgress } issue) return;
        var original = _draft.Text; var text = original.Trim();
        if (text.Length is < 1 or > 2000) { _state.Text = "메시지를 1~2,000자로 입력하세요."; return; }
        _sending = true; UpdateAvailability();
        try
        {
            await _api.SendIssueMessageAsync(issue.Id, text);
            if (IsDisposed) return;
            if (_issue?.Id == issue.Id && _draft.Text == original) _draft.Clear();
            if (_drafts.GetValueOrDefault(issue.Id) == original) _drafts.Remove(issue.Id);
            await PollAsync();
        }
        catch (Exception ex) { if (!IsDisposed) _state.Text = "전송 결과 확인 필요 · 입력 유지: " + ex.Message; }
        finally { _sending = false; if (!IsDisposed) { var state = _state.Text; UpdateAvailability(); if (state.StartsWith("전송 결과")) _state.Text = state; } }
    }

    private void Render()
    {
        var text = _issue != null && _messages.TryGetValue(_issue.Id, out var messages)
            ? string.Concat(messages.Values.Select(m => $"[{(m.SenderRole == UserRole.Admin ? "관리자" : "작업자")} · {m.SenderName} · {m.CreatedAt.ToLocalTime():HH:mm:ss}]\r\n{m.Message}\r\n\r\n")) : "";
        if (_history.Text == text) return;
        _history.Text = text; _history.SelectionStart = _history.TextLength; _history.ScrollToCaret();
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
