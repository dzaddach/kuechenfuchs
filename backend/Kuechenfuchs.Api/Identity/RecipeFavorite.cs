namespace Kuechenfuchs.Api.Identity;

public sealed class RecipeFavorite
{
    public Guid HouseholdId { get; set; }
    public Guid RecipeId { get; set; }
}
