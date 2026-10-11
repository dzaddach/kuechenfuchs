using System.Text.Json;

namespace Kuechenfuchs.Api.ChatGpt;

/// <summary>
/// Enforces an upper bound for the serialized ChatGPT action responses (OpenAI: payloads must stay below
/// 100,000 characters). The bound is measured on the exact JSON string that is sent, i.e. including escaping
/// and JSON overhead. Normal responses are returned unchanged; oversized ones are shortened step by step and
/// marked <c>truncated</c>. Only the response copy is shortened – stored recipes are never modified.
/// </summary>
public static class ChatGptResponseLimit
{
    public const int MaxCharacters = 90_000;
    private const string Ellipsis = " …";
    private static readonly int[] StepCaps = [1_000, 500, 250, 120];
    private const int IngredientNameCap = 60;
    private const int DescriptionCap = 200;
    private const int NameCap = 90;

    public static string FitDetail(ChatGptRecipeDetail detail, JsonSerializerOptions options, int maxCharacters = MaxCharacters)
    {
        var json = JsonSerializer.Serialize(detail, options);
        if (json.Length <= maxCharacters)
        {
            return json;
        }

        var current = detail with { Truncated = true };
        foreach (var cap in StepCaps)
        {
            current = current with { Steps = current.Steps.Select(step => Cut(step, cap)).ToList() };
            if (TryFit(current, options, maxCharacters, out json))
            {
                return json;
            }
        }

        current = current with
        {
            Ingredients = current.Ingredients.Select(item => item with { Name = Cut(item.Name, IngredientNameCap) }).ToList(),
        };
        if (TryFit(current, options, maxCharacters, out json))
        {
            return json;
        }

        current = current with { Description = Cut(current.Description, DescriptionCap), Tags = [] };
        if (TryFit(current, options, maxCharacters, out json))
        {
            return json;
        }

        // Keep the longest fitting prefix of the steps (ingredients complete), then of the ingredients.
        var steps = current.Steps;
        var withoutSteps = current with { Steps = [] };
        if (TryFit(withoutSteps, options, maxCharacters, out _))
        {
            return LargestFittingPrefix(steps.Count, count => current with { Steps = steps.Take(count).ToList() }, options, maxCharacters);
        }

        var ingredients = current.Ingredients;
        var withoutIngredients = withoutSteps with { Ingredients = [] };
        if (TryFit(withoutIngredients, options, maxCharacters, out _))
        {
            return LargestFittingPrefix(
                ingredients.Count,
                count => withoutSteps with { Ingredients = ingredients.Take(count).ToList() },
                options,
                maxCharacters);
        }

        var minimal = withoutIngredients with { Description = string.Empty, Name = Cut(current.Name, NameCap) };
        if (TryFit(minimal, options, maxCharacters, out json))
        {
            return json;
        }

        // Unreachable for data within the recipe validation limits; never return an oversized response.
        throw new InvalidOperationException("The recipe response cannot be reduced below the configured limit.");
    }

    public static string FitList(ChatGptRecipeList list, JsonSerializerOptions options, int maxCharacters = MaxCharacters)
    {
        var json = JsonSerializer.Serialize(list, options);
        if (json.Length <= maxCharacters)
        {
            return json;
        }

        var recipes = list.Recipes;
        var shortened = list with { Truncated = true };
        return LargestFittingPrefix(recipes.Count, count => shortened with { Recipes = recipes.Take(count).ToList() }, options, maxCharacters);
    }

    /// <summary>Shortens text without splitting a UTF-16 surrogate pair and marks the cut.</summary>
    public static string Cut(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length] + Ellipsis;
    }

    private static bool TryFit<T>(T value, JsonSerializerOptions options, int maxCharacters, out string json)
    {
        json = JsonSerializer.Serialize(value, options);
        return json.Length <= maxCharacters;
    }

    /// <summary>Binary search for the largest prefix length whose serialization fits; prefix 0 must fit.</summary>
    private static string LargestFittingPrefix<T>(int count, Func<int, T> create, JsonSerializerOptions options, int maxCharacters)
    {
        var low = 0;
        var high = count;
        var best = JsonSerializer.Serialize(create(0), options);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (TryFit(create(middle), options, maxCharacters, out var json))
            {
                low = middle;
                best = json;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }
}
