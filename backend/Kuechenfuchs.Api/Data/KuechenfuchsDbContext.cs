using Kuechenfuchs.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Data;

public sealed class KuechenfuchsDbContext(
    DbContextOptions<KuechenfuchsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeFavorite> RecipeFavorites => Set<RecipeFavorite>();
    public DbSet<AiUsageBudget> AiUsageBudgets => Set<AiUsageBudget>();
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<Unit> Units => Set<Unit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.IsEnabled).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
        });

        builder.Entity<Household>(entity =>
        {
            entity.HasKey(household => household.Id);
            entity.Property(household => household.Name).HasMaxLength(120).IsRequired();
            entity.Property(household => household.CreatedAt).IsRequired();
        });

        builder.Entity<HouseholdMember>(entity =>
        {
            entity.HasKey(member => new { member.HouseholdId, member.UserId });
            entity.Property(member => member.Role).HasMaxLength(32).IsRequired();
            entity.HasIndex(member => member.UserId);
            entity.HasOne(member => member.Household)
                .WithMany(household => household.Members)
                .HasForeignKey(member => member.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Recipe>(entity =>
        {
            entity.HasKey(recipe => recipe.Id);
            entity.Property(recipe => recipe.Name).HasMaxLength(90).IsRequired();
            entity.Property(recipe => recipe.Description).HasMaxLength(500).IsRequired();
            entity.Property(recipe => recipe.Category).HasMaxLength(40).IsRequired();
            entity.Property(recipe => recipe.Tags).HasColumnType("text[]").IsRequired();
            entity.Property(recipe => recipe.Version).IsConcurrencyToken().IsRequired();
            entity.Property(recipe => recipe.CreatedAt).IsRequired();
            entity.Property(recipe => recipe.UpdatedAt).IsRequired();
            entity.HasIndex(recipe => new { recipe.HouseholdId, recipe.Name });
            entity.HasAlternateKey(recipe => new { recipe.HouseholdId, recipe.Id });
            entity.HasOne(recipe => recipe.Household)
                .WithMany()
                .HasForeignKey(recipe => recipe.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(ingredient => ingredient.Id);
            entity.Property(ingredient => ingredient.Name).HasMaxLength(120).IsRequired();
            entity.Property(ingredient => ingredient.Amount).HasPrecision(12, 3);
            entity.Property(ingredient => ingredient.UnitCode).HasMaxLength(20).IsRequired();
            entity.HasIndex(ingredient => new { ingredient.RecipeId, ingredient.Position }).IsUnique();
            entity.HasOne(ingredient => ingredient.Recipe)
                .WithMany(recipe => recipe.Ingredients)
                .HasForeignKey(ingredient => ingredient.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(ingredient => ingredient.Food)
                .WithMany()
                .HasForeignKey(ingredient => ingredient.FoodId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Unit>()
                .WithMany()
                .HasForeignKey(ingredient => ingredient.UnitCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RecipeStep>(entity =>
        {
            entity.HasKey(step => step.Id);
            entity.Property(step => step.Description).HasMaxLength(2_000).IsRequired();
            entity.HasIndex(step => new { step.RecipeId, step.Position }).IsUnique();
            entity.HasOne(step => step.Recipe)
                .WithMany(recipe => recipe.Steps)
                .HasForeignKey(step => step.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecipeFavorite>(entity =>
        {
            entity.HasKey(favorite => new { favorite.HouseholdId, favorite.RecipeId });
            entity.HasOne<Recipe>()
                .WithMany(recipe => recipe.Favorites)
                .HasForeignKey(favorite => new { favorite.HouseholdId, favorite.RecipeId })
                .HasPrincipalKey(recipe => new { recipe.HouseholdId, recipe.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiUsageBudget>(entity =>
        {
            entity.HasKey(usage => new { usage.HouseholdId, usage.MonthStart });
            entity.Property(usage => usage.SpentUsd).HasPrecision(14, 8).IsRequired();
            entity.Property(usage => usage.ReservedUsd).HasPrecision(14, 8).IsRequired();
            entity.Property(usage => usage.ReservationUpdatedAt);
            entity.Property(usage => usage.RequestCount).IsRequired();
            entity.HasOne<Household>()
                .WithMany()
                .HasForeignKey(usage => usage.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Food>(entity =>
        {
            entity.HasKey(food => food.Id);
            entity.Property(food => food.Name).HasMaxLength(120).IsRequired();
            entity.Property(food => food.Category).HasMaxLength(40).IsRequired();
            entity.HasIndex(food => food.Name).IsUnique();
        });

        builder.Entity<Unit>(entity =>
        {
            entity.HasKey(unit => unit.Code);
            entity.Property(unit => unit.Code).HasMaxLength(20);
            entity.Property(unit => unit.Name).HasMaxLength(60).IsRequired();
            entity.Property(unit => unit.Dimension).HasMaxLength(20).IsRequired();
            entity.HasData(
                new Unit { Code = "g", Name = "Gramm", Dimension = "mass" },
                new Unit { Code = "kg", Name = "Kilogramm", Dimension = "mass" },
                new Unit { Code = "ml", Name = "Milliliter", Dimension = "volume" },
                new Unit { Code = "l", Name = "Liter", Dimension = "volume" },
                new Unit { Code = "piece", Name = "Stück", Dimension = "count" },
                new Unit { Code = "tbsp", Name = "Esslöffel", Dimension = "volume" },
                new Unit { Code = "tsp", Name = "Teelöffel", Dimension = "volume" },
                new Unit { Code = "pinch", Name = "Prise", Dimension = "count" });
        });

        builder.Entity<Food>().HasData(
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc001"), Name = "Nudeln", Category = "Trockenwaren" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc002"), Name = "Tomaten", Category = "Gemüse" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc003"), Name = "Zwiebeln", Category = "Gemüse" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc004"), Name = "Mehl", Category = "Backen" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc005"), Name = "Parmesan", Category = "Käse" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc006"), Name = "Olivenöl", Category = "Öl und Essig" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc007"), Name = "Naturjoghurt", Category = "Milchprodukte" },
            new Food { Id = Guid.Parse("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc008"), Name = "Karotten", Category = "Gemüse" });
    }
}
