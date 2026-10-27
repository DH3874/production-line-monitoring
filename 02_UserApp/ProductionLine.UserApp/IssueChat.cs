using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private readonly RichTextBox _issueChatMessages = Messages();
    private readonly TextBox _issueChatInput = ChatInput();
    private readonly Button _issueChatSend = new() { Text = "전송", AutoSize = true };
    private readonly Label _issueChatState = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Dictionary<Guid, string> _issueChatDrafts = new();
    private Guid? _issueChatDraftIssue;

    private Control BuildIssueChatPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        panel.Controls.Add(_issueChatState, 0, 0); panel.Controls.Add(_issueChatMessages, 0, 1);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _issueChatInput.MaxLength = 2000;
        _issueChatInput.PlaceholderText = "선택한 문제의 관리자와 대화하세요. 조치 중에만 전송할 수 있습니다.";
        composer.Controls.Add(_issueChatInput, 0, 0); composer.Controls.Add(_issueChatSend, 1, 0);
        panel.Controls.Add(composer, 0, 2);
        _issueChatSend.Click += async (_, _) => await SendIssueChatMessageAsync();
        UpdateIssueChatAvailability();
        return panel;
    }

    private Task SendIssueChatMessageAsync() => SendChatMessageAsync(SelectedIssue(), _issueChatInput);

    private void ChangeIssueChat()
    {
        var id = SelectedIssue()?.Id;
        if (_issueChatDraftIssue != id)
        {
            if (_issueChatDraftIssue is Guid previous) _issueChatDrafts[previous] = _issueChatInput.Text;
            _issueChatDraftIssue = id;
            _issueChatInput.Text = id is Guid current && _issueChatDrafts.TryGetValue(current, out var draft) ? draft : "";
            RenderIssueChat();
        }
        UpdateIssueChatAvailability();
    }

    private void UpdateIssueChatAvailability()
    {
        var issue = SelectedIssue();
        var allowed = issue?.Status == IssueStatus.InProgress;
        _issueChatSend.Enabled = allowed && !_sendingMessage;
        _issueChatInput.ReadOnly = !allowed || _sendingMessage;
        _issueChatState.Text = issue == null ? "왼쪽에서 문제를 선택하세요." :
            $"{issue.Title} · {StatusText(issue.Status)}\r\n" +
            (allowed ? "관리자와 채팅 가능 · 5초마다 새 답변 확인" : "대화는 읽기 전용입니다. 점검 시작 후 조치 중에만 전송할 수 있습니다.");
    }

    private void RenderIssueChat()
    {
        var issue = SelectedIssue();
        var text = issue == null ? "문제를 선택하세요." :
            _serverMessages.TryGetValue(issue.Id, out var messages) && messages.Count > 0
                ? string.Concat(messages.Values.Select(m => $"[{(m.SenderRole == UserRole.Admin ? "관리자" : "작업자")} · {m.SenderName} · {m.CreatedAt.ToLocalTime():MM-dd HH:mm}]\r\n{m.Message}\r\n\r\n"))
                : "아직 대화가 없습니다. 새 답변을 자동으로 확인합니다.";
        if (_issueChatMessages.Text == text) return;
        _issueChatMessages.Text = text;
        _issueChatMessages.SelectionStart = _issueChatMessages.TextLength;
        _issueChatMessages.ScrollToCaret();
    }
}
