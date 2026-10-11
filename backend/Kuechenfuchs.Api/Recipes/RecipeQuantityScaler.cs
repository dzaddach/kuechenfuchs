namespace Kuechenfuchs.Api.Recipes;

public static class RecipeQuantityScaler
{
    public static decimal? Scale(decimal? amount, int baseServings, int targetServings)
    {
        if (baseServings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseServings), "Base servings must be positive.");
        }

        if (targetServings <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetServings), "Target servings must be positive.");
        }

        return amount is null ? null : amount.Value * targetServings / baseServings;
    }
}
