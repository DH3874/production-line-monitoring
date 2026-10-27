namespace ProductionLine.Shared;

// General chat does not require a persisted issue. Existing consultation DTOs stay unchanged.
public sealed record AiChatRequest(int LineId, Guid ConversationId, string Question);
public sealed record AiChatStatus(bool AiConfigured, string Provider);
