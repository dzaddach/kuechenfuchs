using System.Text.Json;
using Kuechenfuchs.Api.Ai;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class AiRecipeDraftValidatorTests
{
    private static readonly HashSet<string> Units = ["g", "piece"];

    [Fact]
    public void AcceptsValidDraftAndKeepsUnknownQuantitiesUnspecified()
    {
        const string json = """
            {
              "title": "Kartoffelpfanne",
              "description": "Ein schnelles Abendessen.",
              "servings": 3,
              "preparationMinutes": 25,
              "category": "Abendessen",
              "tags": ["schnell"],
              "ingredients": [
                { "displayName": "Kartoffeln", "amount": 500, "unitCode": "g", "foodSuggestion": null },
                { "displayName": "Kräuter", "amount": null, "unitCode": "piece", "foodSuggestion": "Frische Kräuter" }
              ],
              "steps": ["Kartoffeln braten."],
              "warnings": []
            }
            """;

        var valid = AiRecipeDraftValidator.TryParse(json, Units, 3, out var draft);

        Assert.True(valid);
        Assert.NotNull(draft);
        Assert.Equal(500m, draft.Recipe.Ingredients[0].Amount);
        Assert.Null(draft.Recipe.Ingredients[1].Amount);
        Assert.Null(draft.Recipe.Ingredients[1].FoodId);
        Assert.Contains(draft.Warnings, warning => warning.Contains("Zutatenmenge ist nicht angegeben", StringComparison.Ordinal));
        Assert.Contains(draft.Warnings, warning => warning.Contains("nicht automatisch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"title":"","description":"","servings":3,"preparationMinutes":25,"category":"Abendessen","tags":[],"ingredients":[],"steps":[],"warnings":[]}""")]
    [InlineData("""{"title":"Meal","description":"","servings":0,"preparationMinutes":25,"category":"Abendessen","tags":[],"ingredients":[{"displayName":"Potato","amount":2,"unitCode":"g","foodSuggestion":null}],"steps":["Cook"],"warnings":[]}""")]
    [InlineData("""{"title":"Meal","description":"","servings":3,"preparationMinutes":25,"category":"Abendessen","tags":[],"ingredients":[{"displayName":"Potato","amount":0,"unitCode":"g","foodSuggestion":null}],"steps":["Cook"],"warnings":[]}""")]
    [InlineData("""{"title":"Meal","description":"","servings":3,"preparationMinutes":25,"category":"Abendessen","tags":[],"ingredients":[{"displayName":"Potato","amount":2,"unitCode":"cup","foodSuggestion":null}],"steps":["Cook"],"warnings":[]}""")]
    [InlineData("""{"title":"Meal","description":"","servings":3,"preparationMinutes":25,"category":"Dinner","tags":[],"ingredients":[{"displayName":"Potato","amount":2,"unitCode":"g","foodSuggestion":null}],"steps":["Cook"],"warnings":[]}""")]
    public void RejectsInvalidJsonSchemaOrRecipeValues(string json)
    {
        Assert.False(AiRecipeDraftValidator.TryParse(json, Units, null, out _));
    }

    [Fact]
    public void RejectsUnitThatIsNotPresentInTheApplicationCatalog()
    {
        const string json = """
            {
              "title": "Meal", "description": "", "servings": 2, "preparationMinutes": 20,
              "category": "Abendessen", "tags": [],
              "ingredients": [{"displayName":"Potato","amount":2,"unitCode":"ml","foodSuggestion":null}],
              "steps": ["Cook"], "warnings": []
            }
            """;

        Assert.False(AiRecipeDraftValidator.TryParse(json, Units, null, out _));
    }

    [Fact]
    public void EnforcesRequestedServingsAndThreeDecimalPlaces()
    {
        const string json = """
            {
              "title": "Meal", "description": "", "servings": 2, "preparationMinutes": 20,
              "category": "Abendessen", "tags": [],
              "ingredients": [{"displayName":"Potato","amount":1.0001,"unitCode":"g","foodSuggestion":null}],
              "steps": ["Cook"], "warnings": []
            }
            """;

        Assert.False(AiRecipeDraftValidator.TryParse(json, Units, 3, out _));
        Assert.False(AiRecipeDraftValidator.TryParse(json.Replace("1.0001", "1.001"), Units, 3, out _));
    }

    [Fact]
    public void ReservesCostFromConfiguredTokenCeilingsAndRates()
    {
        var settings = new AiRecipeOptions
        {
            MaxInputTokens = 8_000,
            MaxOutputTokens = 1_000,
            InputUsdPerMillionTokens = 0.5m,
            OutputUsdPerMillionTokens = 1.5m
        };

        Assert.Equal(0.0055m, AiRecipeDraftValidator.MaximumRequestCost(settings));
        Assert.Equal(0.0008m, AiRecipeDraftValidator.ActualCost(settings, 1_000, 200));
    }

    [Fact]
    public void RoundsConfiguredCostsUpToThePersistedDatabasePrecision()
    {
        var settings = new AiRecipeOptions
        {
            MaxInputTokens = 1,
            MaxOutputTokens = 1,
            InputUsdPerMillionTokens = 0.001m,
            OutputUsdPerMillionTokens = 0.001m
        };

        Assert.Equal(0.00000001m, AiRecipeDraftValidator.MaximumRequestCost(settings));
        Assert.Equal(0.00000001m, AiRecipeDraftValidator.ActualCost(settings, 1, 0));
    }
}
