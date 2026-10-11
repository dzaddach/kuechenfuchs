namespace Kuechenfuchs.Api.Ai;

public interface IAiRecipeDraftService
{
    Task<AiRecipeDraftResult> CreateDraftAsync(
        Guid householdId,
        AiRecipeDraftRequest request,
        CancellationToken cancellationToken);
}

public sealed record AiRecipeDraftResult(
    AiRecipeDraftResponse? Draft,
    AiRecipeDraftStatus Status);

public enum AiRecipeDraftStatus
{
    Success,
    InvalidRequest,
    NotConfigured,
    RequestLimitReached,
    ProviderRateLimited,
    BudgetReached,
    ProviderUnavailable,
    ProviderRefused,
    InvalidProviderResponse
}
