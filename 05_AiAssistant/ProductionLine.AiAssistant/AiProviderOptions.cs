namespace ProductionLine.AiAssistant;

public class AiProviderOptions
{
    public AiProvider Provider { get; init; } = AiProvider.OpenAi;
    public bool Enabled { get; init; } = true;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gpt-5.4-mini";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(7);
    public int MaxOutputTokens { get; init; } = 900;
    public int RequestsPerMinute { get; init; } = 20;
    public int RequestsPerDay { get; init; } = 100;
    public string GeminiThinkingLevel { get; init; } = "MINIMAL";
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    internal void Validate()
    {
        if (!Enum.IsDefined(Provider) || string.IsNullOrWhiteSpace(Model) || RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromSeconds(30)
            || MaxOutputTokens is < 100 or > 2048 || RequestsPerMinute < 1 || RequestsPerDay < 1)
            throw new ArgumentException("AI 설정의 모델·응답 제한·호출 제한을 확인하세요.");
    }
}

public enum AiProvider { OpenAi, Gemini }
