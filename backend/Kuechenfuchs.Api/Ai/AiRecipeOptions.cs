namespace Kuechenfuchs.Api.Ai;

public sealed class AiRecipeOptions
{
    public bool Enabled { get; set; }
    public int RequestsPerMinutePerUser { get; set; } = 8;
    public int MaxMonthlyRequestsPerHousehold { get; set; } = 100;
    public decimal MonthlyBudgetUsd { get; set; }
    public int MaxInputTokens { get; set; } = 8_000;
    public int MaxOutputTokens { get; set; } = 1_500;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxPromptCharacters { get; set; } = 2_000;
    public decimal InputUsdPerMillionTokens { get; set; }
    public decimal OutputUsdPerMillionTokens { get; set; }
    public OpenAiOptions OpenAI { get; set; } = new();

    public sealed class OpenAiOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4.1-mini";
        public string? BaseUrl { get; set; }
    }

    public bool IsOperational => Enabled
        && !string.IsNullOrWhiteSpace(OpenAI.ApiKey)
        && !string.IsNullOrWhiteSpace(OpenAI.Model)
        && MonthlyBudgetUsd > 0
        && InputUsdPerMillionTokens > 0
        && OutputUsdPerMillionTokens > 0
        && MaxMonthlyRequestsPerHousehold > 0
        && RequestsPerMinutePerUser > 0
        && MaxInputTokens > 0
        && MaxOutputTokens > 0
        && TimeoutSeconds is >= 1 and <= 120
        && MaxPromptCharacters is >= 20 and <= 10_000;
}
