namespace ProductionLine.AiAssistant;

public sealed class AiResponseClient(HttpClient http)
{
    internal Task<AiGeneratedAnswer> GenerateAsync(AiProviderOptions options, object issue,
        IReadOnlyList<AiManualReference> references, IReadOnlyList<AiChatTurn> history, string question, CancellationToken cancellationToken)
        => options.Provider switch
        {
            AiProvider.Gemini => new GeminiResponsesClient(http).GenerateAsync(options, issue, references, history, question, cancellationToken),
            AiProvider.OpenAi => new OpenAiResponsesClient(http).GenerateAsync(options, issue, references, history, question, cancellationToken),
            _ => throw new InvalidOperationException("지원하지 않는 AI 제공자입니다.")
        };
}
