using System.Security.Claims;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Recipes;

public static class RecipeEndpoints
{
    private const int MaxIngredients = 100;
    private const int MaxSteps = 100;
    private const int MaxTags = 20;

    public static RouteGroupBuilder MapRecipeEndpoints(this RouteGroupBuilder api)
    {
        var recipes = api.MapGroup("/households/{householdId:guid}/recipes");
        recipes.MapGet("", ListAsync);
        recipes.MapGet("/{recipeId:guid}", GetAsync);
        recipes.MapPost("", CreateAsync);
        recipes.MapPut("/{recipeId:guid}", UpdateAsync);
        recipes.MapDelete("/{recipeId:guid}", ArchiveAsync);
        recipes.MapPut("/{recipeId:guid}/favorite", AddFavoriteAsync);
        recipes.MapDelete("/{recipeId:guid}/favorite", RemoveFavoriteAsync);

        var catalog = api.MapGroup("/households/{householdId:guid}");
        catalog.MapGet("/recipe-favorites", GetFavoritesAsync);
        catalog.MapGet("/units", GetUnitsAsync);
        catalog.MapGet("/foods", SearchFoodsAsync);
        catalog.MapGet("/foods/{foodId:guid}", GetFoodAsync);
        return api;
    }

    private static async Task<IResult> GetFavoritesAsync(
        Guid householdId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var favorites = await database.RecipeFavorites.AsNoTracking()
            .Where(favorite => favorite.HouseholdId == householdId)
            .Select(favorite => favorite.RecipeId)
            .ToListAsync(cancellationToken);
        return Results.Ok(favorites);
    }

    private static async Task<IResult> AddFavoriteAsync(
        Guid householdId,
        Guid recipeId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var recipeExists = await database.Recipes.AnyAsync(
            recipe => recipe.HouseholdId == householdId && recipe.Id == recipeId && !recipe.IsArchived,
            cancellationToken);
        if (!recipeExists)
        {
            return Results.NotFound();
        }

        await database.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"RecipeFavorites\" (\"HouseholdId\", \"RecipeId\") VALUES ({householdId}, {recipeId}) ON CONFLICT DO NOTHING",
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveFavoriteAsync(
        Guid householdId,
        Guid recipeId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var recipeExists = await database.Recipes.AnyAsync(
            recipe => recipe.HouseholdId == householdId && recipe.Id == recipeId,
            cancellationToken);
        if (!recipeExists)
        {
            return Results.NotFound();
        }

        await database.RecipeFavorites
            .Where(favorite => favorite.HouseholdId == householdId && favorite.RecipeId == recipeId)
            .ExecuteDeleteAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        Guid householdId,
        string? q,
        string? category,
        bool? includeArchived,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var query = database.Recipes.AsNoTracking()
            .Where(recipe => recipe.HouseholdId == householdId);
        if (includeArchived != true)
        {
            query = query.Where(recipe => !recipe.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var normalized = q.Trim();
            query = query.Where(recipe =>
                EF.Functions.ILike(recipe.Name, $"%{normalized}%")
                || EF.Functions.ILike(recipe.Description, $"%{normalized}%")
                || recipe.Ingredients.Any(ingredient => EF.Functions.ILike(ingredient.Name, $"%{normalized}%"))
                || recipe.Tags.Any(tag => EF.Functions.ILike(tag, $"%{normalized}%")));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(recipe => recipe.Category == category.Trim());
        }

        var results = await query
            .OrderBy(recipe => recipe.Name)
            .Take(100)
            .Select(recipe => new RecipeSummaryDto(
                recipe.Id,
                recipe.Name,
                recipe.Description,
                recipe.BaseServings,
                recipe.PreparationMinutes,
                recipe.Category,
                recipe.Tags,
                recipe.IsArchived,
                recipe.Version))
            .ToListAsync(cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetAsync(
        Guid householdId,
        Guid recipeId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var recipe = await database.Recipes.AsNoTracking()
            .Where(item => item.HouseholdId == householdId && item.Id == recipeId)
            .Include(item => item.Ingredients)
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.ETag = ToEtag(recipe.Version);
        return Results.Ok(await ToDetailAsync(database, recipe, cancellationToken));
    }

    private static async Task<IResult> CreateAsync(
        Guid householdId,
        RecipeWriteDto request,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var validation = await ValidateAsync(database, request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var recipe = CreateRecipe(householdId, request);
        database.Recipes.Add(recipe);
        await database.SaveChangesAsync(cancellationToken);
        context.Response.Headers.ETag = ToEtag(recipe.Version);
        return Results.Created($"/api/v1/households/{householdId}/recipes/{recipe.Id}",
            await ToDetailAsync(database, recipe, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        Guid householdId,
        Guid recipeId,
        RecipeWriteDto request,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        if (!TryReadIfMatch(context.Request.Headers.IfMatch.ToString(), out var ifMatch))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match is required");
        }

        var recipe = await database.Recipes
            .Where(item => item.HouseholdId == householdId && item.Id == recipeId && !item.IsArchived)
            .Include(item => item.Ingredients)
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        if (recipe.Version != ifMatch)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        var validation = await ValidateAsync(database, request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        ApplyWrite(recipe, request);
        recipe.Version = Guid.NewGuid();
        recipe.UpdatedAt = DateTimeOffset.UtcNow;

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            ReplaceChildren(database, recipe, request);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        context.Response.Headers.ETag = ToEtag(recipe.Version);
        return Results.Ok(await ToDetailAsync(database, recipe, cancellationToken));
    }

    private static async Task<IResult> ArchiveAsync(
        Guid householdId,
        Guid recipeId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        if (!TryReadIfMatch(context.Request.Headers.IfMatch.ToString(), out var ifMatch))
        {
            return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match is required");
        }

        var recipe = await database.Recipes
            .SingleOrDefaultAsync(item => item.HouseholdId == householdId && item.Id == recipeId && !item.IsArchived, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        if (recipe.Version != ifMatch)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        recipe.IsArchived = true;
        recipe.Version = Guid.NewGuid();
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        context.Response.Headers.ETag = ToEtag(recipe.Version);
        return Results.NoContent();
    }

    private static async Task<IResult> GetUnitsAsync(
        Guid householdId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        return Results.Ok(await database.Units.AsNoTracking().OrderBy(unit => unit.Code)
            .Select(unit => new UnitDto(unit.Code, unit.Name, unit.Dimension))
            .ToListAsync(cancellationToken));
    }

    private static async Task<IResult> SearchFoodsAsync(
        Guid householdId,
        string? q,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var foods = database.Foods.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            foods = foods.Where(food => EF.Functions.ILike(food.Name, $"%{search}%"));
        }

        return Results.Ok(await foods.OrderBy(food => food.Name).Take(50)
            .Select(food => new FoodDto(food.Id, food.Name, food.Category))
            .ToListAsync(cancellationToken));
    }

    private static async Task<IResult> GetFoodAsync(
        Guid householdId,
        Guid foodId,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(principal);
        if (userId is null || !await IsMemberAsync(database, householdId, userId.Value, cancellationToken))
        {
            return Results.NotFound();
        }

        var food = await database.Foods.AsNoTracking().SingleOrDefaultAsync(item => item.Id == foodId, cancellationToken);
        return food is null ? Results.NotFound() : Results.Ok(new FoodDto(food.Id, food.Name, food.Category));
    }

    private static async Task<IResult?> ValidateAsync(
        KuechenfuchsDbContext database,
        RecipeWriteDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 90
            || request.Description is null
            || request.Description.Length > 500
            || request.BaseServings is < 1 or > 50
            || request.PreparationMinutes is < 1 or > 600
            || string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 40
            || request.Ingredients is null || request.Ingredients.Count is < 1 or > MaxIngredients
            || request.Steps is null || request.Steps.Count is < 1 or > MaxSteps
            || request.Tags is null || request.Tags.Count > MaxTags
            || request.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Trim().Length > 40))
        {
            return InvalidRecipe();
        }

        if (request.Ingredients.Any(ingredient =>
                ingredient is null || string.IsNullOrWhiteSpace(ingredient.Name) || ingredient.Name.Trim().Length > 120
                || string.IsNullOrWhiteSpace(ingredient.UnitCode) || ingredient.UnitCode.Length > 20
                || ingredient.Amount is <= 0 or > 1_000_000
                || ingredient.Amount is { } amount && decimal.Round(amount, 3) != amount
                || ingredient.FoodId == Guid.Empty))
        {
            return InvalidRecipe();
        }

        if (request.Steps.Any(step => step is null || string.IsNullOrWhiteSpace(step.Description) || step.Description.Trim().Length > 2_000))
        {
            return InvalidRecipe();
        }

        var requestedUnits = request.Ingredients.Select(item => item.UnitCode).Distinct().ToList();
        if (requestedUnits.Count != await database.Units.CountAsync(unit => requestedUnits.Contains(unit.Code), cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown unit code");
        }

        var foodIds = request.Ingredients.Where(item => item.FoodId.HasValue).Select(item => item.FoodId!.Value).Distinct().ToList();
        if (foodIds.Count != await database.Foods.CountAsync(food => foodIds.Contains(food.Id), cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown food");
        }

        return null;
    }

    private static Recipe CreateRecipe(Guid householdId, RecipeWriteDto request)
    {
        var recipe = new Recipe
        {
            HouseholdId = householdId,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            BaseServings = request.BaseServings,
            PreparationMinutes = request.PreparationMinutes,
            Category = request.Category.Trim(),
            Tags = NormalizeTags(request.Tags),
        };
        AddChildren(recipe, request);
        return recipe;
    }

    private static void ApplyWrite(Recipe recipe, RecipeWriteDto request)
    {
        recipe.Name = request.Name.Trim();
        recipe.Description = request.Description.Trim();
        recipe.BaseServings = request.BaseServings;
        recipe.PreparationMinutes = request.PreparationMinutes;
        recipe.Category = request.Category.Trim();
        recipe.Tags = NormalizeTags(request.Tags);
    }

    private static void ReplaceChildren(
        KuechenfuchsDbContext database,
        Recipe recipe,
        RecipeWriteDto request)
    {
        database.RecipeIngredients.RemoveRange(recipe.Ingredients);
        database.RecipeSteps.RemoveRange(recipe.Steps);
        recipe.Ingredients.Clear();
        recipe.Steps.Clear();
        var replacementIngredients = request.Ingredients.Select((ingredient, position) => new RecipeIngredient
        {
            Position = position,
            Name = ingredient.Name.Trim(),
            Amount = ingredient.Amount,
            UnitCode = ingredient.UnitCode.Trim(),
            FoodId = ingredient.FoodId,
        }).ToList();
        var replacementSteps = request.Steps.Select((step, position) => new RecipeStep
        {
            Position = position,
            Description = step.Description.Trim(),
        }).ToList();
        database.RecipeIngredients.AddRange(replacementIngredients);
        database.RecipeSteps.AddRange(replacementSteps);
        foreach (var ingredient in replacementIngredients)
        {
            recipe.Ingredients.Add(ingredient);
        }

        foreach (var step in replacementSteps)
        {
            recipe.Steps.Add(step);
        }
    }

    private static void AddChildren(Recipe recipe, RecipeWriteDto request)
    {
        foreach (var (ingredient, position) in request.Ingredients.Select((item, index) => (item, index)))
        {
            recipe.Ingredients.Add(new RecipeIngredient
            {
                Position = position,
                Name = ingredient.Name.Trim(),
                Amount = ingredient.Amount,
                UnitCode = ingredient.UnitCode.Trim(),
                FoodId = ingredient.FoodId,
            });
        }

        foreach (var (step, position) in request.Steps.Select((item, index) => (item, index)))
        {
            recipe.Steps.Add(new RecipeStep { Position = position, Description = step.Description.Trim() });
        }
    }

    private static async Task<RecipeDetailDto> ToDetailAsync(
        KuechenfuchsDbContext database,
        Recipe recipe,
        CancellationToken cancellationToken)
    {
        var foodIds = recipe.Ingredients.Where(item => item.FoodId.HasValue).Select(item => item.FoodId!.Value).Distinct().ToList();
        var foods = await database.Foods.AsNoTracking().Where(food => foodIds.Contains(food.Id))
            .ToDictionaryAsync(food => food.Id, cancellationToken);
        return new RecipeDetailDto(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.BaseServings,
            recipe.PreparationMinutes,
            recipe.Category,
            recipe.Tags,
            recipe.IsArchived,
            recipe.Version,
            recipe.Ingredients.OrderBy(item => item.Position)
                .Select(item => new RecipeIngredientDto(
                    item.Id,
                    item.Position,
                    item.Name,
                    item.Amount,
                    item.UnitCode,
                    item.FoodId,
                    item.FoodId is { } foodId && foods.TryGetValue(foodId, out var food) ? food.Name : null))
                .ToList(),
            recipe.Steps.OrderBy(item => item.Position)
                .Select(item => new RecipeStepDto(item.Id, item.Position, item.Description))
                .ToList());
    }

    private static string[] NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(tag => tag.Trim()).Where(tag => tag.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tag => tag).ToArray();

    private static Guid? GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static Task<bool> IsMemberAsync(
        KuechenfuchsDbContext database,
        Guid householdId,
        Guid userId,
        CancellationToken cancellationToken) =>
        database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId && member.UserId == userId,
            cancellationToken);

    private static bool TryReadIfMatch(string value, out Guid version)
    {
        var raw = value.Trim().Trim('"');
        return Guid.TryParse(raw, out version);
    }

    private static string ToEtag(Guid version) => $"\"{version:D}\"";

    private static IResult InvalidRecipe() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid recipe data");

    public sealed record RecipeWriteDto(
        string Name,
        string Description,
        int BaseServings,
        int PreparationMinutes,
        string Category,
        List<string> Tags,
        List<RecipeIngredientWriteDto> Ingredients,
        List<RecipeStepWriteDto> Steps);

    public sealed record RecipeIngredientWriteDto(string Name, decimal? Amount, string UnitCode, Guid? FoodId);
    public sealed record RecipeStepWriteDto(string Description);
    public sealed record RecipeSummaryDto(Guid Id, string Name, string Description, int BaseServings,
        int PreparationMinutes, string Category, string[] Tags, bool IsArchived, Guid Version);
    public sealed record RecipeDetailDto(Guid Id, string Name, string Description, int BaseServings,
        int PreparationMinutes, string Category, string[] Tags, bool IsArchived, Guid Version,
        List<RecipeIngredientDto> Ingredients, List<RecipeStepDto> Steps);
    public sealed record RecipeIngredientDto(Guid Id, int Position, string Name, decimal? Amount,
        string UnitCode, Guid? FoodId, string? FoodName);
    public sealed record RecipeStepDto(Guid Id, int Position, string Description);
    public sealed record UnitDto(string Code, string Name, string Dimension);
    public sealed record FoodDto(Guid Id, string Name, string Category);
}
