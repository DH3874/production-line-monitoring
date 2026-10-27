using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private readonly System.Windows.Forms.Timer _messageRefresh = new() { Interval = 5000 };
    private readonly Dictionary<Guid, SortedDictionary<long, IssueMessageDto>> _serverMessages = new();
    private readonly HashSet<Guid> _messagesLoaded = new();
    private readonly Dictionary<Guid, long> _messageCursors = new();
    private bool _pollingMessages, _sendingMessage;

    private void MergeAdminMessages(Guid issueId, IEnumerable<IssueMessageDto> messages)
    {
        if (!_serverMessages.TryGetValue(issueId, out var cache))
            _serverMessages[issueId] = cache = new();
        foreach (var message in messages.Where(x => x.IssueId == issueId)) cache[message.Id] = message;
    }

    private async Task PollAdminMessagesAsync()
    {
        if (_pollingMessages || IsDisposed || SessionExpired || SelectedIssue() is not IssueDto issue) return;
        _pollingMessages = true;
        try
        {
            long? afterId = _messageCursors.TryGetValue(issue.Id, out var cursor) ? cursor : null;
            var messages = await _api.GetIssueMessagesAsync(issue.Id, afterId);
            if (IsDisposed) return;
            MergeAdminMessages(issue.Id, messages);
            if (messages.Count > 0) _messageCursors[issue.Id] = Math.Max(afterId ?? 0, messages.Max(x => x.Id));
            var firstLoad = _messagesLoaded.Add(issue.Id);
            if (SelectedIssue()?.Id == issue.Id && (firstLoad || messages.Count > 0)) RenderIssueChat();
        }
        catch (Exception ex) { await HandleServerFailureAsync(ex, "관리자 대화 조회 실패"); }
        finally { _pollingMessages = false; }
    }

    private async Task SendChatMessageAsync(IssueDto? issue, TextBox input)
    {
        if (_sendingMessage || issue == null) return;
        if (issue.Status != IssueStatus.InProgress) { UpdateIssueChatAvailability(); return; }
        var original = input.Text;
        var text = original.Trim();
        if (text.Length is < 1 or > 2000) { _connection.Text = "메시지는 1~2000자로 입력하세요."; return; }
        _sendingMessage = true;
        UpdateIssueChatAvailability();
        try
        {
            var message = await _api.SendIssueMessageAsync(issue.Id, text);
            if (IsDisposed) return;
            MergeAdminMessages(issue.Id, new[] { message });
            if (_issueChatDraftIssue == issue.Id && input.Text == original) input.Clear();
            if (_issueChatDrafts.GetValueOrDefault(issue.Id) == original) _issueChatDrafts.Remove(issue.Id);
            RenderIssueChat();
            _connection.Text = "관리자에게 전송했습니다. 답장을 5초마다 확인합니다.";
            await PollAdminMessagesAsync();
        }
        catch (Exception ex)
        {
            await HandleServerFailureAsync(ex, "메시지 전송 결과를 확인하지 못했습니다 · 입력 유지됨 · 대화 확인 후 재전송하세요", refreshIssues: true);
        }
        finally { _sendingMessage = false; if (!IsDisposed) UpdateIssueChatAvailability(); }
    }
}
