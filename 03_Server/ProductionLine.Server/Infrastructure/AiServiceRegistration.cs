using ProductionLine.AiAssistant;

namespace ProductionLine.Server.Infrastructure;

internal static class AiServiceRegistration
{
    public static IServiceCollection AddProductionLineConsultation(this IServiceCollection services, IConfiguration configuration)
    {
        var providerName = configuration["AI_PROVIDER"] ?? configuration["Ai:Provider"] ?? "Gemini";
        if (!Enum.TryParse<AiProvider>(providerName, true, out var provider))
            throw new InvalidOperationException("AI_PROVIDER는 Gemini 또는 OpenAi로 설정하세요.");
        var section = provider == AiProvider.Gemini ? "Gemini" : "OpenAI";
        var options = new AiProviderOptions
        {
            Provider = provider,
            ApiKey = configuration[provider == AiProvider.Gemini ? "GEMINI_API_KEY" : "OPENAI_API_KEY"]
                ?? configuration[$"{section}:ApiKey"] ?? string.Empty,
            Model = configuration[provider == AiProvider.Gemini ? "GEMINI_MODEL" : "OPENAI_MODEL"]
                ?? configuration[$"{section}:Model"]
                ?? (provider == AiProvider.Gemini ? "gemini-3.5-flash-lite" : "gpt-5.4-mini"),
            Enabled = configuration.GetValue($"{section}:Enabled", configuration.GetValue("Ai:Enabled", true)),
            // 데스크톱 클라이언트의 10초 제한 안에 대체 답변을 반환할 여유를 둡니다.
            RequestTimeout = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Ai:TimeoutSeconds", 7), 1, 8)),
            MaxOutputTokens = Math.Clamp(configuration.GetValue("Ai:MaxOutputTokens", 2048), 100, 2048),
            RequestsPerMinute = Math.Max(1, configuration.GetValue("Ai:RequestsPerMinute", 20)),
            RequestsPerDay = Math.Max(1, configuration.GetValue("Ai:RequestsPerDay", 100)),
            GeminiThinkingLevel = configuration["Gemini:ThinkingLevel"] ?? "MINIMAL"
        };
        services.AddSingleton(options);
        services.AddSingleton<ManualAssistant>();
        services.AddHttpClient<AiResponseClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<AiConsultationService>>();
            return new AiConsultationService(
                serviceProvider.GetRequiredService<ManualAssistant>(),
                serviceProvider.GetRequiredService<AiResponseClient>(), options,
                code => logger.LogInformation("AI consultation event: {EventCode}", code));
        });
        return services;
    }
}
