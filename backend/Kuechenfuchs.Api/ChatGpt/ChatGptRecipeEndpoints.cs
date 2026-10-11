using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kuechenfuchs.Api.Ai;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.ExternalIdentity;
using Kuechenfuchs.Api.Identity;
using Kuechenfuchs.Api.Recipes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuechenfuchs.Api.ChatGpt;

/// <summary>
/// Narrow ChatGPT Actions for the linked household. Reads need <c>recipes:read</c>; drafts and the two-step
/// save need <c>recipes:write</c>. The household always comes from the verified Keycloak link. Recipes are
/// stored only by a separate, single-use confirmation call (marked consequential in the action schema).
/// All recipe logic (search, validation, creation, AI drafts) is the existing PWA logic.
/// </summary>
public static class ChatGptRecipeEndpoints
{
    public const int MaxListResults = 50;
    public const int SaveRequestLifetimeMinutes = 15;
    public const int MaxPendingSaveRequests = 10;
    private const int MaxSearchLength = 100;
    private const int MaxCategoryLength = 40;
    private const int MaxSummaryDescriptionLength = 300;
    private static readonly EventId RecipeSaved = new(5201, nameof(RecipeSaved));
    private static readonly EventId SaveRequested = new(5202, nameof(SaveRequested));
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapChatGptRecipeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/chatgpt")
            .RequireAuthorization(ChatGptBearer.AuthenticatedPolicy)
            .RequireLinkedHousehold()
            .WithTags("ChatGPT actions");
        group.MapGet("/recipes", ListAsync)
            .RequireAuthorization(ChatGptBearer.RecipesReadPolicy)
            .WithName("listRecipes");
        group.MapGet("/recipes/{recipeId:guid}", GetAsync)
            .RequireAuthorization(ChatGptBearer.RecipesReadPolicy)
            .WithName("getRecipe");
        group.MapPost("/recipe-drafts", DraftAsync)
            .RequireAuthorization(ChatGptBearer.RecipesWritePolicy)
            .WithName("createRecipeDraft");
        group.MapPost("/recipe-save-requests", CreateSaveRequestAsync)
            .RequireAuthorization(ChatGptBearer.RecipesWritePolicy)
            .WithName("requestRecipeSave");
        group.MapPost("/recipe-save-requests/{confirmationId:guid}/confirm", ConfirmSaveAsync)
            .RequireAuthorization(ChatGptBearer.RecipesWritePolicy)
            .WithName("confirmRecipeSave");
        return group;
    }

    private static async Task<IResult> ListAsync(
        string? q,
        string? category,
        int? limit,
        HttpContext context,
        IOptions<JsonOptions> jsonOptions,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        if (q?.Length > MaxSearchLength || category?.Length > MaxCategoryLength || limit is < 1 or > MaxListResults)
        {
            return InvalidInput();
        }

        var linked = context.GetLinkedHousehold();
        var query = RecipeEndpoints.Search(database.Recipes.AsNoTracking(), linked.HouseholdId, q, category, includeArchived: false);
        var count = await query.CountAsync(cancellationToken);
        var recipes = await query
            .OrderBy(recipe => recipe.Name)
            .Take(limit ?? 20)
            .Select(recipe => new ChatGptRecipeSummary(
                recipe.Id,
                recipe.Name,
                recipe.Description.Length > MaxSummaryDescriptionLength
                    ? recipe.Description.Substring(0, MaxSummaryDescriptionLength)
                    : recipe.Description,
                recipe.BaseServings,
                recipe.PreparationMinutes,
                recipe.Category,
                recipe.Tags))
            .ToListAsync(cancellationToken);
        var householdName = await database.Households.AsNoTracking()
            .Where(household => household.Id == linked.HouseholdId)
            .Select(household => household.Name)
            .SingleAsync(cancellationToken);
        // Exactly the measured JSON is sent; oversized result lists are cut to a fitting prefix (truncated=true).
        return Json(ChatGptResponseLimit.FitList(
            new ChatGptRecipeList(householdName, count, count > recipes.Count, recipes),
            jsonOptions.Value.SerializerOptions));
    }

    private static async Task<IResult> GetAsync(
        Guid recipeId,
        int? servings,
        HttpContext context,
        IOptions<JsonOptions> jsonOptions,
        KuechenfuchsDbContext database,
        CancellationToken cancellationToken)
    {
        if (servings is < 1 or > 50)
        {
            return InvalidInput();
        }

        var linked = context.GetLinkedHousehold();
        var recipe = await database.Recipes.AsNoTracking()
            .Where(item => item.HouseholdId == linked.HouseholdId && item.Id == recipeId && !item.IsArchived)
            .Include(item => item.Ingredients)
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        var targetServings = servings ?? recipe.BaseServings;
        var ingredients = recipe.Ingredients.OrderBy(item => item.Position)
            .Select(item => new ChatGptIngredient(
                item.Name,
                RecipeQuantityScaler.Scale(item.Amount, recipe.BaseServings, targetServings) is { } amount
                    ? decimal.Round(amount, 3)
                    : null,
                item.UnitCode))
            .ToList();
        var steps = recipe.Steps.OrderBy(item => item.Position).Select(item => item.Description).ToList();
        // The serialized response (incl. escaping and JSON overhead) is bounded; only this copy is shortened.
        return Json(ChatGptResponseLimit.FitDetail(new ChatGptRecipeDetail(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.BaseServings,
            targetServings,
            recipe.PreparationMinutes,
            recipe.Category,
            recipe.Tags,
            ingredients,
            steps,
            Truncated: false), jsonOptions.Value.SerializerOptions));
    }

    private static async Task<IResult> DraftAsync(
        ChatGptDraftRequest? request,
        HttpContext context,
        IAiRecipeDraftService draftService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return InvalidInput();
        }

        var linked = context.GetLinkedHousehold();
        // Same service, validation, budget and provider as the PWA; the draft is never persisted.
        var result = await draftService.CreateDraftAsync(
            linked.HouseholdId,
            new AiRecipeDraftRequest(
                request.Prompt,
                request.DesiredServings,
                request.AvailableIngredients,
                request.Preferences,
                request.AvoidIngredients,
                request.ExternalProcessingConfirmed),
            cancellationToken);
        return AiRecipeEndpoints.ToResult(result);
    }

    private static async Task<IResult> CreateSaveRequestAsync(
        ChatGptRecipeInput? input,
        HttpContext context,
        KuechenfuchsDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return InvalidInput();
        }

        var write = ToWriteDto(input);
        var validation = await RecipeEndpoints.ValidateAsync(database, write, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var linked = context.GetLinkedHousehold();
        var now = DateTimeOffset.UtcNow;
        await database.ChatGptRecipeSaveRequests
            .Where(request => request.ExpiresAt < now.AddDays(-1))
            .ExecuteDeleteAsync(cancellationToken);
        var pending = await database.ChatGptRecipeSaveRequests.CountAsync(
            request => request.LinkId == linked.LinkId && request.ConfirmedAt == null && request.ExpiresAt > now,
            cancellationToken);
        if (pending >= MaxPendingSaveRequests)
        {
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many unconfirmed save requests");
        }

        var payload = JsonSerializer.Serialize(Normalize(write), PayloadJson);
        var saveRequest = new ChatGptRecipeSaveRequest
        {
            LinkId = linked.LinkId,
            UserId = linked.UserId,
            HouseholdId = linked.HouseholdId,
            PayloadJson = payload,
            PayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(SaveRequestLifetimeMinutes),
        };
        database.ChatGptRecipeSaveRequests.Add(saveRequest);
        await database.SaveChangesAsync(cancellationToken);
        loggerFactory.CreateLogger(typeof(ChatGptRecipeEndpoints).FullName!).LogInformation(
            SaveRequested,
            "ChatGPT save request {SaveRequestId} created for household {HouseholdId} via link {LinkId}.",
            saveRequest.Id,
            linked.HouseholdId,
            linked.LinkId);

        return Results.Ok(new ChatGptSaveRequestResponse(
            saveRequest.Id,
            "pending-confirmation",
            saveRequest.ExpiresAt,
            new ChatGptSaveRequestPreview(
                write.Name.Trim(),
                write.BaseServings,
                write.PreparationMinutes,
                write.Category.Trim(),
                write.Ingredients.Count,
                write.Steps.Count),
            "Noch NICHT gespeichert. Zeige dem Benutzer Name, Portionen, Zutaten und Schritte und frage ausdrücklich, ob das Rezept in Küchenfuchs gespeichert werden soll. Nur nach einem eindeutigen Ja confirmRecipeSave mit confirmed=true aufrufen."));
    }

    private static async Task<IResult> ConfirmSaveAsync(
        Guid confirmationId,
        ChatGptConfirmSaveRequest? request,
        HttpContext context,
        KuechenfuchsDbContext database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is not { Confirmed: true })
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Explicit confirmation required");
        }

        var linked = context.GetLinkedHousehold();
        var now = DateTimeOffset.UtcNow;
        // Bound to the same link: after disconnecting/re-linking, old confirmations are unusable.
        var saveRequest = await database.ChatGptRecipeSaveRequests.AsNoTracking()
            .Where(item => item.Id == confirmationId && item.LinkId == linked.LinkId && item.HouseholdId == linked.HouseholdId)
            .Select(item => new { item.PayloadJson, item.ExpiresAt, item.ConfirmedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (saveRequest is null)
        {
            return Results.NotFound();
        }

        if (saveRequest.ConfirmedAt is not null)
        {
            return await AlreadySavedAsync(database, confirmationId, cancellationToken);
        }

        if (saveRequest.ExpiresAt <= now)
        {
            return Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Save request expired; request a new confirmation");
        }

        var write = JsonSerializer.Deserialize<RecipeEndpoints.RecipeWriteDto>(saveRequest.PayloadJson, PayloadJson)!;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Single use: the row is consumed atomically; a concurrent or repeated confirmation waits for this
        // transaction and then finds it consumed.
        var consumed = await database.ChatGptRecipeSaveRequests
            .Where(item => item.Id == confirmationId && item.ConfirmedAt == null && item.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConfirmedAt, now), cancellationToken);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await AlreadySavedAsync(database, confirmationId, cancellationToken);
        }

        var validation = await RecipeEndpoints.ValidateAsync(database, write, cancellationToken);
        if (validation is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return validation;
        }

        var recipe = RecipeEndpoints.CreateRecipe(linked.HouseholdId, write);
        database.Recipes.Add(recipe);
        await database.SaveChangesAsync(cancellationToken);
        await database.ChatGptRecipeSaveRequests
            .Where(item => item.Id == confirmationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RecipeId, recipe.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        loggerFactory.CreateLogger(typeof(ChatGptRecipeEndpoints).FullName!).LogInformation(
            RecipeSaved,
            "ChatGPT save request {SaveRequestId} confirmed: recipe {RecipeId} saved for household {HouseholdId} via link {LinkId}.",
            confirmationId,
            recipe.Id,
            linked.HouseholdId,
            linked.LinkId);
        return Results.Created(
            $"/api/v1/chatgpt/recipes/{recipe.Id}",
            new ChatGptSaveResult("saved", recipe.Id, recipe.Name, "Das Rezept wurde in Küchenfuchs gespeichert."));
    }

    private static async Task<IResult> AlreadySavedAsync(
        KuechenfuchsDbContext database,
        Guid confirmationId,
        CancellationToken cancellationToken)
    {
        var saved = await database.ChatGptRecipeSaveRequests.AsNoTracking()
            .Where(item => item.Id == confirmationId)
            .Select(item => new { item.RecipeId })
            .SingleAsync(cancellationToken);
        var name = saved.RecipeId is { } recipeId
            ? await database.Recipes.AsNoTracking().Where(recipe => recipe.Id == recipeId).Select(recipe => recipe.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        return Results.Json(
            new ChatGptSaveResult(
                "already-saved",
                saved.RecipeId ?? Guid.Empty,
                name ?? string.Empty,
                "Diese Bestätigung wurde bereits verwendet; das Rezept wurde nicht erneut gespeichert."),
            statusCode: StatusCodes.Status409Conflict);
    }

    private static RecipeEndpoints.RecipeWriteDto ToWriteDto(ChatGptRecipeInput input) =>
        new(
            input.Name,
            input.Description,
            input.BaseServings,
            input.PreparationMinutes,
            input.Category,
            input.Tags,
            input.Ingredients?.Select(item => item is null
                    ? null!
                    : new RecipeEndpoints.RecipeIngredientWriteDto(item.Name, item.Amount, item.UnitCode, FoodId: null))
                .ToList()!,
            input.Steps?.Select(step => step is null ? null! : new RecipeEndpoints.RecipeStepWriteDto(step)).ToList()!);

    private static RecipeEndpoints.RecipeWriteDto Normalize(RecipeEndpoints.RecipeWriteDto write) =>
        write with
        {
            Name = write.Name.Trim(),
            Description = write.Description.Trim(),
            Category = write.Category.Trim(),
            Tags = write.Tags.Select(tag => tag.Trim()).ToList(),
            Ingredients = write.Ingredients
                .Select(item => item with { Name = item.Name.Trim(), UnitCode = item.UnitCode.Trim() })
                .ToList(),
            Steps = write.Steps.Select(step => step with { Description = step.Description.Trim() }).ToList(),
        };

    private static IResult Json(string json) => Results.Text(json, "application/json; charset=utf-8");

    private static IResult InvalidInput() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
}
