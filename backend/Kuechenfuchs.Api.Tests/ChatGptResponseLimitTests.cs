using System.Text.Json;
using Kuechenfuchs.Api.ChatGpt;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class ChatGptResponseLimitTests
{
    // Same defaults as ASP.NET Core minimal APIs (Web, default encoder that escapes <, >, ", non-ASCII …).
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void NormalRecipeIsReturnedCompletelyAndUnchanged()
    {
        var detail = Recipe(steps: 8, stepLength: 200, ingredients: 12, nameLength: 30);

        var json = ChatGptResponseLimit.FitDetail(detail, Web);

        Assert.Equal(JsonSerializer.Serialize(detail, Web), json);
        var parsed = Parse(json);
        Assert.False(parsed.Truncated);
        Assert.Equal(detail.Steps, parsed.Steps);
        Assert.Equal(detail.Ingredients, parsed.Ingredients);
    }

    [Theory]
    [InlineData('a')]
    [InlineData('<')]   // escaped as < (6 characters)
    [InlineData('"')]   // escaped as \"
    [InlineData('ü')]   // non-ASCII, escaped as ü
    public void MaximumSizeRecipeIsBoundedValidAndMarkedTruncated(char filler)
    {
        // Upper validation limits: 100 steps × 2,000 characters, 100 ingredients × 120 characters, 20 tags.
        var detail = Recipe(steps: 100, stepLength: 2_000, ingredients: 100, nameLength: 120, filler: filler);

        var json = ChatGptResponseLimit.FitDetail(detail, Web);

        Assert.True(json.Length <= ChatGptResponseLimit.MaxCharacters, $"{json.Length} characters");
        var parsed = Parse(json);
        Assert.True(parsed.Truncated);
        Assert.Equal(detail.Id, parsed.Id);
        Assert.Equal(detail.Name, parsed.Name);
        Assert.NotEmpty(parsed.Steps);
        Assert.StartsWith(detail.Steps[0][..50], parsed.Steps[0]);
    }

    [Fact]
    public void ManyShortStepsAndLongIngredientNamesAreBounded()
    {
        var detail = Recipe(steps: 100, stepLength: 119, ingredients: 100, nameLength: 120, filler: '"');

        var json = ChatGptResponseLimit.FitDetail(detail, Web, maxCharacters: 20_000);

        Assert.True(json.Length <= 20_000);
        Assert.True(Parse(json).Truncated);
    }

    [Theory]
    [InlineData(20_000)]
    [InlineData(5_000)]
    [InlineData(1_500)]
    public void SmallLimitsStillProduceCompleteJsonWithinTheLimit(int limit)
    {
        var detail = Recipe(steps: 100, stepLength: 2_000, ingredients: 100, nameLength: 120, filler: '<');

        var json = ChatGptResponseLimit.FitDetail(detail, Web, limit);

        Assert.True(json.Length <= limit, $"{json.Length} > {limit}");
        var parsed = Parse(json);
        Assert.True(parsed.Truncated);
        Assert.Equal(detail.Id, parsed.Id);
    }

    [Fact]
    public void InputIsNotModified()
    {
        var detail = Recipe(steps: 100, stepLength: 2_000, ingredients: 100, nameLength: 120);
        var stepsBefore = detail.Steps.ToList();
        var ingredientsBefore = detail.Ingredients.ToList();

        ChatGptResponseLimit.FitDetail(detail, Web, 5_000);

        Assert.False(detail.Truncated);
        Assert.Equal(stepsBefore, detail.Steps);
        Assert.Equal(ingredientsBefore, detail.Ingredients);
    }

    [Fact]
    public void CutNeverSplitsSurrogatePairs()
    {
        var emojis = string.Concat(Enumerable.Repeat("🍅", 50));

        foreach (var length in new[] { 1, 2, 3, 11, 99 })
        {
            var cut = ChatGptResponseLimit.Cut(emojis, length);
            var withoutEllipsis = cut[..^2];
            Assert.False(withoutEllipsis.Length > 0 && char.IsHighSurrogate(withoutEllipsis[^1]), $"split at {length}");
        }

        var detail = Recipe(steps: 100, stepLength: 2_000, ingredients: 10, nameLength: 20) with
        {
            Steps = Enumerable.Repeat(string.Concat(Enumerable.Repeat("🍅", 1_000)), 100).ToList(),
        };
        var json = ChatGptResponseLimit.FitDetail(detail, Web);
        Assert.True(json.Length <= ChatGptResponseLimit.MaxCharacters);
        Assert.True(Parse(json).Truncated);
    }

    [Fact]
    public void LargeListIsCutToAFittingPrefixAndKeepsTheTotalCount()
    {
        var recipes = Enumerable.Range(0, 50).Select(index => new ChatGptRecipeSummary(
            Guid.NewGuid(),
            new string('<', 90),
            new string('"', 300),
            4,
            30,
            new string('ü', 40),
            Enumerable.Repeat(new string('<', 40), 20).ToArray())).ToList();
        var list = new ChatGptRecipeList("Haushalt", 120, true, recipes);

        var json = ChatGptResponseLimit.FitList(list, Web);

        Assert.True(json.Length <= ChatGptResponseLimit.MaxCharacters, $"{json.Length} characters");
        var parsed = JsonSerializer.Deserialize<ChatGptRecipeList>(json, Web)!;
        Assert.True(parsed.Truncated);
        Assert.Equal(120, parsed.Count);
        Assert.InRange(parsed.Recipes.Count, 1, 49);
        Assert.Equal(recipes[0].Id, parsed.Recipes[0].Id);
    }

    [Fact]
    public void NormalListIsReturnedUnchanged()
    {
        var list = new ChatGptRecipeList("Haushalt", 2, false,
        [
            new(Guid.NewGuid(), "Lasagne", "Klassisch", 4, 60, "Abendessen", ["nudeln"]),
            new(Guid.NewGuid(), "Suppe", "Warm", 2, 20, "Mittag", []),
        ]);

        Assert.Equal(JsonSerializer.Serialize(list, Web), ChatGptResponseLimit.FitList(list, Web));
    }

    private static ChatGptRecipeDetail Parse(string json) => JsonSerializer.Deserialize<ChatGptRecipeDetail>(json, Web)!;

    private static ChatGptRecipeDetail Recipe(int steps, int stepLength, int ingredients, int nameLength, char filler = 'a') =>
        new(
            Guid.NewGuid(),
            "Riesenrezept",
            new string(filler, 500),
            4,
            6,
            60,
            "Abendessen",
            Enumerable.Range(0, 20).Select(index => $"tag-{index}-{new string(filler, 30)}").ToArray(),
            Enumerable.Range(0, ingredients).Select(index => new ChatGptIngredient($"{index}:{new string(filler, nameLength - 4)}", 1.5m, "g")).ToList(),
            Enumerable.Range(0, steps).Select(index => $"Schritt {index}: {new string(filler, stepLength - 12)}").ToList(),
            false);
}
