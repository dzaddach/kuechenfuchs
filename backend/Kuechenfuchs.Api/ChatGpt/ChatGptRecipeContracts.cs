using System.Text.Json.Serialization;

namespace Kuechenfuchs.Api.ChatGpt;

// Contracts of the ChatGPT recipe actions. Input types reject unknown members (no mass assignment of
// householdId, id, version, archive state, food IDs …); the household always comes from the verified link.

public sealed record ChatGptRecipeSummary(
    Guid Id,
    string Name,
    string Description,
    int BaseServings,
    int PreparationMinutes,
    string Category,
    string[] Tags);

public sealed record ChatGptRecipeList(string HouseholdName, int Count, bool Truncated, IReadOnlyList<ChatGptRecipeSummary> Recipes);

public sealed record ChatGptIngredient(string Name, decimal? Amount, string UnitCode);

public sealed record ChatGptRecipeDetail(
    Guid Id,
    string Name,
    string Description,
    int BaseServings,
    int Servings,
    int PreparationMinutes,
    string Category,
    string[] Tags,
    IReadOnlyList<ChatGptIngredient> Ingredients,
    IReadOnlyList<string> Steps,
    bool Truncated);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatGptDraftRequest(
    string Prompt,
    int? DesiredServings,
    string[]? AvailableIngredients,
    string[]? Preferences,
    string[]? AvoidIngredients,
    bool ExternalProcessingConfirmed);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatGptIngredientInput(string Name, decimal? Amount, string UnitCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatGptRecipeInput(
    string Name,
    string Description,
    int BaseServings,
    int PreparationMinutes,
    string Category,
    List<string> Tags,
    List<ChatGptIngredientInput> Ingredients,
    List<string> Steps);

public sealed record ChatGptSaveRequestPreview(
    string Name,
    int BaseServings,
    int PreparationMinutes,
    string Category,
    int IngredientCount,
    int StepCount);

public sealed record ChatGptSaveRequestResponse(
    Guid ConfirmationId,
    string Status,
    DateTimeOffset ExpiresAt,
    ChatGptSaveRequestPreview Recipe,
    string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChatGptConfirmSaveRequest(bool Confirmed);

public sealed record ChatGptSaveResult(string Status, Guid RecipeId, string Name, string Message);
