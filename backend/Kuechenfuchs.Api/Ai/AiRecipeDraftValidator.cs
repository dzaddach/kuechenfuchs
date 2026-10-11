using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kuechenfuchs.Api.Ai;

public static class AiRecipeDraftValidator
{
    private static readonly HashSet<string> UnitCodes =
        ["g", "kg", "ml", "l", "piece", "tbsp", "tsp", "pinch"];
    private static readonly HashSet<string> Categories =
        ["Abendessen", "Frühstück", "Mittagessen", "Dessert", "Snack", "Vegetarisch", "Vegan", "Fisch"];

    public static bool TryParse(
        string? content,
        IReadOnlySet<string> availableUnitCodes,
        int? requestedServings,
        out AiRecipeDraftResponse? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(content) || content.Length > 100_000)
        {
            return false;
        }

        AiRecipePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AiRecipePayload>(
                content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    AllowDuplicateProperties = false
                });
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null
            || !HasText(payload.Title, 90)
            || payload.Description is null || payload.Description.Length > 500
            || payload.Servings is < 1 or > 50
            || (requestedServings.HasValue && payload.Servings != requestedServings.Value)
            || payload.PreparationMinutes is < 1 or > 600
            || !HasText(payload.Category, 40)
            || !Categories.Contains(payload.Category!)
            || payload.Tags is null || payload.Tags.Length > 20
            || payload.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 40)
            || payload.Ingredients is null || payload.Ingredients.Length is < 1 or > 100
            || payload.Steps is null || payload.Steps.Length is < 1 or > 100
            || payload.Steps.Any(step => string.IsNullOrWhiteSpace(step) || step.Length > 2_000)
            || payload.Warnings is null || payload.Warnings.Length > 10
            || payload.Warnings.Any(warning => string.IsNullOrWhiteSpace(warning) || warning.Length > 300))
        {
            return false;
        }

        var warnings = payload.Warnings.ToList();
        var ingredients = new AiRecipeDraftIngredient[payload.Ingredients.Length];
        for (var index = 0; index < payload.Ingredients.Length; index++)
        {
            var ingredient = payload.Ingredients[index];
            if (!HasText(ingredient.DisplayName, 120)
                || string.IsNullOrWhiteSpace(ingredient.UnitCode)
                || !UnitCodes.Contains(ingredient.UnitCode)
                || !availableUnitCodes.Contains(ingredient.UnitCode)
                || (ingredient.Amount.HasValue
                    && (ingredient.Amount.Value <= 0
                        || ingredient.Amount.Value > 100_000
                        || decimal.Round(ingredient.Amount.Value, 3) != ingredient.Amount.Value)))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ingredient.FoodSuggestion))
            {
                if (ingredient.FoodSuggestion.Length > 120)
                {
                    return false;
                }

                warnings.Add($"„{ingredient.DisplayName!}“ wurde nicht automatisch einem Lebensmittel zugeordnet.");
            }

            ingredients[index] = new AiRecipeDraftIngredient(
                ingredient.DisplayName!.Trim(),
                ingredient.Amount,
                ingredient.UnitCode!,
                null);
        }

        if (ingredients.Any(ingredient => !ingredient.Amount.HasValue))
        {
            warnings.Insert(0, "Mindestens eine Zutatenmenge ist nicht angegeben; bitte vor dem Kochen prüfen.");
        }

        response = new AiRecipeDraftResponse(
            new AiRecipeDraftRecipe(
                payload.Title!.Trim(),
                payload.Description.Trim(),
                payload.Servings,
                payload.PreparationMinutes,
                payload.Category!.Trim(),
                payload.Tags!.Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ingredients,
                payload.Steps!.Select(step => new AiRecipeDraftStep(step.Trim())).ToArray()),
            warnings.Take(10).ToArray());
        return true;
    }

    public static decimal MaximumRequestCost(AiRecipeOptions settings) =>
        (settings.MaxInputTokens * settings.InputUsdPerMillionTokens
            + settings.MaxOutputTokens * settings.OutputUsdPerMillionTokens) / 1_000_000m;

    public static decimal ActualCost(AiRecipeOptions settings, int promptTokens, int completionTokens) =>
        (promptTokens * settings.InputUsdPerMillionTokens
            + completionTokens * settings.OutputUsdPerMillionTokens) / 1_000_000m;

    private static bool HasText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;
}
