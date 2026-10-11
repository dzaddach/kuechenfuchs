namespace Kuechenfuchs.Api.Identity;

public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public int BaseServings { get; set; }
    public int PreparationMinutes { get; set; }
    public required string Category { get; set; }
    public string[] Tags { get; set; } = [];
    public bool IsArchived { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<RecipeIngredient> Ingredients { get; } = new List<RecipeIngredient>();
    public ICollection<RecipeStep> Steps { get; } = new List<RecipeStep>();
}

public sealed class RecipeIngredient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int Position { get; set; }
    public required string Name { get; set; }
    public decimal? Amount { get; set; }
    public required string UnitCode { get; set; }
    public Guid? FoodId { get; set; }
    public Food? Food { get; set; }
}

public sealed class RecipeStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int Position { get; set; }
    public required string Description { get; set; }
}

public sealed class Food
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Category { get; set; } = string.Empty;
}

public sealed class Unit
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Dimension { get; set; }
}
