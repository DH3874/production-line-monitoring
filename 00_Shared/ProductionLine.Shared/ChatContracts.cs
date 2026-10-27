namespace ProductionLine.Shared;

public sealed record SendChatMessageRequest(Guid ClientMessageId, string Text);

public sealed record ChatMessageDto(long Id, Guid ClientMessageId, string SenderId, string SenderName,
    UserRole SenderRole, string Text, DateTimeOffset SentAt);

public sealed record ChatMessagePage(IReadOnlyList<ChatMessageDto> Messages, long LatestMessageId,
    bool HasMore, bool HistoryTruncated, bool ResetRequired);
