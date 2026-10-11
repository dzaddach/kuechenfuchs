using Kuechenfuchs.Api.Recipes;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class RecipeQuantityScalerTests
{
    [Fact]
    public void ScalesIngredientAmountByServingRatio()
    {
        Assert.Equal(500m, RecipeQuantityScaler.Scale(250m, 2, 4));
    }

    [Fact]
    public void PreservesFractionalPrecisionUntilDisplay()
    {
        Assert.Equal(0.4995m, RecipeQuantityScaler.Scale(0.333m, 2, 3));
    }

    [Fact]
    public void KeepsMissingAmountMissing()
    {
        Assert.Null(RecipeQuantityScaler.Scale(null, 2, 4));
    }

    [Theory]
    [InlineData(0, 2, "baseServings")]
    [InlineData(2, 0, "targetServings")]
    public void RejectsNonPositiveServings(int baseServings, int targetServings, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RecipeQuantityScaler.Scale(100m, baseServings, targetServings));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
