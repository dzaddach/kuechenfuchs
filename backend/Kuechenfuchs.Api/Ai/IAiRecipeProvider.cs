namespace Kuechenfuchs.Api.Ai;

public interface IAiRecipeProvider
{
    Task<AiProviderResponse> GenerateAsync(AiProviderInput input, CancellationToken cancellationToken);
}
