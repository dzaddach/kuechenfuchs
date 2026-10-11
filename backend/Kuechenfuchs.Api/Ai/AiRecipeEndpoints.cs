using System.Security.Claims;
using Kuechenfuchs.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuechenfuchs.Api.Ai;

public static class AiRecipeEndpoints
{
    public static RouteGroupBuilder MapAiRecipeEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/households/{householdId:guid}/ai/recipe-drafts", CreateDraftAsync)
            .RequireRateLimiting("ai-draft")
            .WithName("CreateAiRecipeDraft")
            .WithSummary("Generate an unsaved recipe draft")
            .WithDescription("Sends only the user's explicitly confirmed recipe request to the configured AI provider. The response is validated and never persisted.")
            .Produces<AiRecipeDraftResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return api;
    }

    private static async Task<IResult> CreateDraftAsync(
        Guid householdId,
        AiRecipeDraftRequest request,
        ClaimsPrincipal principal,
        KuechenfuchsDbContext database,
        IAiRecipeDraftService draftService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !await database.HouseholdMembers.AnyAsync(
                member => member.HouseholdId == householdId && member.UserId == userId,
                cancellationToken))
        {
            return Results.NotFound();
        }

        var result = await draftService.CreateDraftAsync(householdId, request, cancellationToken);
        return ToResult(result);
    }

    /// <summary>Shared HTTP mapping of draft results (PWA and ChatGPT action).</summary>
    internal static IResult ToResult(AiRecipeDraftResult result)
    {
        return result.Status switch
        {
            AiRecipeDraftStatus.Success when result.Draft is not null => Results.Ok(result.Draft),
            AiRecipeDraftStatus.InvalidRequest => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid recipe draft request"),
            AiRecipeDraftStatus.NotConfigured => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI recipe drafts are not configured"),
            AiRecipeDraftStatus.RequestLimitReached => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Monthly AI request limit reached"),
            AiRecipeDraftStatus.ProviderRateLimited => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "AI provider rate limit reached"),
            AiRecipeDraftStatus.BudgetReached => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Monthly AI budget reached"),
            AiRecipeDraftStatus.ProviderUnavailable => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "AI provider is temporarily unavailable"),
            AiRecipeDraftStatus.ProviderRefused => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "AI provider could not create this recipe"),
            _ => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "AI provider returned an invalid recipe draft")
        };
    }
}
