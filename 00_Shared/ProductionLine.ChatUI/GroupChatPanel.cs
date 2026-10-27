using ProductionLine.Shared;
using System.Runtime.InteropServices;

namespace ProductionLine.ChatUI;

// 관리자·작업자 프로젝트가 같은 화면 소스를 링크해 사용한다.
public sealed class GroupChatPanel : UserControl
{
    private readonly ProductionLineApiClient _login;
    private readonly GroupChatApiClient _api;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedDictionary<long, ChatMessageDto> _messages = new();
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "채팅 연결 대기", TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _notice = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
    private readonly RichTextBox _history = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(248, 250, 253), Font = new Font("맑은 고딕", 10), WordWrap = true,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };
    private readonly TextBox _draft = new()
    {
        Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, MaxLength = 2000,
        ScrollBars = ScrollBars.Vertical, PlaceholderText = "관리자와 작업자 모두에게 보낼 메시지를 입력하세요."
    };
    private readonly Label _count = new() { Dock = DockStyle.Right, Width = 86, Text = "0 / 2,000", TextAlign = ContentAlignment.MiddleRight };
    private readonly CheckBox _autoScroll = new() { Dock = DockStyle.Left, Width = 145, Text = "새 메시지로 이동", Checked = true };
    private readonly Button _send = new() { Dock = DockStyle.Fill, Text = "메시지 전송", BackColor = Color.FromArgb(32, 102, 190), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button _refresh = new() { Dock = DockStyle.Fill, Text = "새로고침", FlatStyle = FlatStyle.Flat };
    private long _lastReceived;
    private bool _started, _loaded, _polling, _sending, _closing;
    private SendChatMessageRequest? _pending;
    private Form? _owner;
    public int UnreadCount { get; private set; }
    public event EventHandler? UnreadChanged;
    private bool _dark;
    // 관리자 앱 어두운 테마용: 채팅 글을 밝은 색으로 그림. 기본값(false)은 작업자 앱의 밝은 채팅 그대로입니다.
    public bool DarkMode
    {
        get => _dark;
        set
        {
            _dark = value;
            _history.BackColor = value ? Color.FromArgb(12, 23, 39) : Color.FromArgb(248, 250, 253);
            _history.ForeColor = BodyColor;
            if (_messages.Count > 0) RenderHistory();
            else _history.ForeColor = BodyColor;
            SetNotice();
        }
    }
    private Color BodyColor => _dark ? Color.FromArgb(236, 242, 249) : Color.FromArgb(34, 45, 60);
    private Color AdminColor => _dark ? Color.FromArgb(110, 170, 255) : Color.FromArgb(32, 102, 190);
    private Color WorkerColor => _dark ? Color.FromArgb(120, 220, 170) : Color.FromArgb(34, 45, 60);
    private Color OkColor => _dark ? Color.FromArgb(61, 214, 140) : Color.FromArgb(22, 130, 85);
    private Color ErrorColor => _dark ? Color.FromArgb(255, 107, 107) : Color.Firebrick;

    public GroupChatPanel(ProductionLineApiClient login)
    {
        _login = login;
        _api = new(login);
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Font = new Font("맑은 고딕", 9.5f);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 34, 32, 26 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 70, 26, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "전체 단체 채팅", Font = new Font(Font.FontFamily, 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        SetNotice();
        layout.Controls.Add(_notice, 0, 1);
        layout.Controls.Add(_status, 0, 2);
        _history.Text = "대화를 불러오는 중입니다. 관리자와 작업자가 함께 사용하는 공용 채팅방입니다.";
        layout.Controls.Add(_history, 0, 3);
        layout.Controls.Add(_draft, 0, 4);
        var footer = new Panel { Dock = DockStyle.Fill };
        footer.Controls.Add(_autoScroll);
        footer.Controls.Add(_count);
        layout.Controls.Add(footer, 0, 5);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        actions.Controls.Add(_send, 0, 0);
        actions.Controls.Add(_refresh, 1, 0);
        layout.Controls.Add(actions, 0, 6);
        Controls.Add(layout);
        _send.Click += async (_, _) => await SendAsync();
        _refresh.Click += async (_, _) => await PollAsync();
        _draft.TextChanged += (_, _) => _count.Text = $"{_draft.TextLength:N0} / 2,000";
        _draft.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || e.Shift) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            await SendAsync();
        };
        _timer.Tick += async (_, _) => await PollAsync();
        VisibleChanged += (_, _) => { if (CanRead()) { MarkRead(); ScrollToLatest(); } };
        _history.SizeChanged += (_, _) =>
        {
            if (!_closing && _history.IsHandleCreated && _autoScroll.Checked)
                _history.BeginInvoke(new Action(ScrollToLatest));
        };
        var tip = new ToolTip();
        tip.SetToolTip(_draft, "Enter: 전송 / Shift+Enter: 줄바꿈. 전체 로그인 사용자에게 공유됩니다.");
        tip.SetToolTip(_history, "처음에는 최근 100건을 불러오며, 이 화면에는 최대 500건을 표시합니다.");
        Disposed += (_, _) => tip.Dispose();
    }

    public void Start()
    {
        if (_started || _closing) return;
        _started = true;
        _owner = FindForm();
        if (_owner is not null) _owner.Activated += OwnerActivated;
        _timer.Start();
        _ = PollAsync();
    }

    private void OwnerActivated(object? sender, EventArgs e) { if (CanRead()) MarkRead(); }
    private bool CanRead() => Visible && FindForm()?.WindowState != FormWindowState.Minimized;
    private void MarkRead()
    {
        if (UnreadCount == 0) return;
        UnreadCount = 0;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetNotice(string? error = null)
    {
        _notice.Text = error ?? "관리자·작업자 모두에게 공유 · Enter 전송 / Shift+Enter 줄바꿈";
        _notice.ForeColor = error is null ? (_dark ? Color.FromArgb(143, 162, 186) : Color.DimGray) : ErrorColor;
    }

    private async Task PollAsync()
    {
        if (_polling || _closing || !_started) return;
        _polling = true;
        var locked = false;
        try
        {
            await _gate.WaitAsync(_lifetime.Token);
            locked = true;
            var changed = false;
            for (var i = 0; i < 5; i++)
            {
                var page = await _api.GetMessagesAsync(_lastReceived, token: _lifetime.Token);
                if (_closing) return;
                if (page.ResetRequired)
                {
                    _messages.Clear();
                    _lastReceived = 0;
                    _loaded = false;
                    MarkRead();
                    changed = true;
                    continue;
                }
                foreach (var message in page.Messages)
                {
                    if (_messages.TryAdd(message.Id, message))
                    {
                        changed = true;
                        if (_loaded && !CanRead() && message.SenderId != _login.CurrentUser?.Id) UnreadCount++;
                    }
                    _lastReceived = Math.Max(_lastReceived, message.Id);
                    if (_pending is { } pending && message.ClientMessageId == pending.ClientMessageId && message.SenderId == _login.CurrentUser?.Id)
                    {
                        if (_draft.Text.Trim() == pending.Text) _draft.Clear();
                        _pending = null;
                        SetNotice();
                    }
                }
                _loaded = true;
                if (!page.HasMore) break;
            }
            while (_messages.Count > 500) _messages.Remove(_messages.Keys.First());
            if (changed || _messages.Count == 0) RenderHistory();
            if (CanRead()) MarkRead();
            UnreadChanged?.Invoke(this, EventArgs.Empty);
            _status.Text = $"● 채팅 연결됨 · 자동 갱신 · {DateTime.Now:HH:mm:ss}";
            _status.ForeColor = OkColor;
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            if (!_closing)
            {
                _status.Text = "● 채팅 연결 확인 필요 · " + FriendlyError(ex);
                _status.ForeColor = ErrorColor;
            }
        }
        finally
        {
            if (locked) _gate.Release();
            _polling = false;
        }
    }

    private async Task SendAsync()
    {
        if (_sending || _closing) return;
        var text = _draft.Text.Trim();
        if (text.Length == 0) { SetNotice("메시지를 입력하세요."); _draft.Focus(); return; }
        if (text.Length > 2000) { SetNotice("메시지는 2,000자 이내로 입력하세요."); return; }
        _sending = true;
        _send.Enabled = false;
        _draft.Enabled = false;
        _send.Text = "전송 중...";
        var locked = false;
        try
        {
            if (_pending?.Text != text) _pending = new(Guid.NewGuid(), text);
            await _gate.WaitAsync(_lifetime.Token);
            locked = true;
            var message = await _api.SendMessageAsync(_pending, _lifetime.Token);
            if (_closing) return;
            _messages[message.Id] = message;
            _pending = null;
            _draft.Clear();
            SetNotice();
            RenderHistory();
            _status.Text = "● 메시지를 전송했습니다.";
            _status.ForeColor = OkColor;
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            if (!_closing)
            {
                SetNotice("전송을 확인하지 못했습니다. 입력 내용은 유지됩니다. 같은 내용으로 다시 전송할 수 있습니다.");
                _status.Text = "● 전송 확인 필요 · " + FriendlyError(ex);
                _status.ForeColor = ErrorColor;
            }
        }
        finally
        {
            if (locked) _gate.Release();
            _sending = false;
            if (!_closing)
            {
                _send.Enabled = true;
                _draft.Enabled = true;
                _send.Text = "메시지 전송";
                _draft.Focus();
            }
        }
        if (!_closing) await PollAsync();
    }

    private void RenderHistory()
    {
        var selected = _history.SelectionStart;
        _history.Clear();
        if (_messages.Count == 0)
        {
            _history.Text = "아직 대화가 없습니다. 첫 메시지를 보내보세요.";
            return;
        }
        using var bold = new Font(_history.Font, FontStyle.Bold);
        foreach (var message in _messages.Values)
        {
            var own = message.SenderId == _login.CurrentUser?.Id;
            _history.SelectionFont = bold;
            _history.SelectionColor = message.SenderRole == UserRole.Admin ? AdminColor : WorkerColor;
            _history.AppendText($"{(own ? "[나] " : "")}{(message.SenderRole == UserRole.Admin ? "관리자" : "작업자")} {message.SenderName} ({message.SenderId}) · {message.SentAt.ToLocalTime():MM/dd HH:mm:ss}\r\n");
            _history.SelectionFont = _history.Font;
            _history.SelectionColor = BodyColor;
            _history.AppendText(message.Text + "\r\n\r\n");
        }
        _history.SelectionStart = _autoScroll.Checked ? _history.TextLength : Math.Min(selected, _history.TextLength);
        _history.SelectionLength = 0;
        if (_autoScroll.Checked) ScrollToLatest();
        else _history.ScrollToCaret();
    }

    private void ScrollToLatest()
    {
        if (_closing || !_autoScroll.Checked || !_history.IsHandleCreated) return;
        _history.Select(_history.TextLength, 0);
        // RichEdit는 숨겨진 탭에서 갱신되거나 크기가 바뀌면 caret만으로 스크롤 위치가 복원되지 않을 수 있다.
        SendMessage(_history.Handle, 0x0115 /* WM_VSCROLL */, new IntPtr(7) /* SB_BOTTOM */, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static string FriendlyError(Exception error) => error switch
    {
        OperationCanceledException => "서버 응답이 지연되고 있습니다.",
        HttpRequestException or IOException => "서버 주소와 연결 상태를 확인하세요.",
        System.Text.Json.JsonException => "서버의 채팅 응답 형식을 확인하세요.",
        _ => error.Message
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _timer.Stop();
            _timer.Dispose();
            if (_owner is not null) _owner.Activated -= OwnerActivated;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _api.Dispose();
            // 진행 중인 await가 gate를 반환할 수 있으므로 여기서 gate를 Dispose하지 않는다.
        }
        base.Dispose(disposing);
    }
}
