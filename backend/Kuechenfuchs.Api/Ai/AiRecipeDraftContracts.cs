namespace Kuechenfuchs.Api.Ai;

/// <summary>Request for an explicitly user-initiated, unsaved AI recipe draft.</summary>
public sealed record AiRecipeDraftRequest(
    string Prompt,
    int? DesiredServings,
    string[]? AvailableIngredients,
    string[]? Preferences,
    string[]? AvoidIngredients,
    bool ExternalProcessingConfirmed);

/// <summary>A recipe draft compatible with the existing recipe creation contract.</summary>
public sealed record AiRecipeDraftRecipe(
    string Name,
    string Description,
    int BaseServings,
    int PreparationMinutes,
    string Category,
    string[] Tags,
    AiRecipeDraftIngredient[] Ingredients,
    AiRecipeDraftStep[] Steps);

/// <summary>An ingredient line with an optional quantity and a stable unit code.</summary>
public sealed record AiRecipeDraftIngredient(
    string Name,
    decimal? Amount,
    string UnitCode,
    Guid? FoodId);

/// <summary>A single preparation instruction.</summary>
public sealed record AiRecipeDraftStep(string Description);

/// <summary>A non-persisted recipe proposal and any cautions returned by validation.</summary>
public sealed record AiRecipeDraftResponse(AiRecipeDraftRecipe Recipe, string[] Warnings);

public sealed record AiProviderInput(
    string Prompt,
    int? DesiredServings,
    string[] AvailableIngredients,
    string[] Preferences,
    string[] AvoidIngredients);

public sealed record AiProviderResponse(
    string? Content,
    int PromptTokens,
    int CompletionTokens,
    string? Refusal,
    AiProviderFailure Failure);

public enum AiProviderFailure
{
    None,
    RateLimited,
    Unavailable,
    InvalidResponse
}

internal sealed record AiRecipePayload(
    string? Title,
    string? Description,
    int Servings,
    int PreparationMinutes,
    string? Category,
    string[]? Tags,
    AiIngredientPayload[]? Ingredients,
    string[]? Steps,
    string[]? Warnings);

internal sealed record AiIngredientPayload(
    string? DisplayName,
    decimal? Amount,
    string? UnitCode,
    string? FoodSuggestion);
