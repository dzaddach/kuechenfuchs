using System.Text;
using System.Text.Json;
using Kuechenfuchs.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuechenfuchs.Api.Ai;

public sealed class AiRecipeDraftService(
    KuechenfuchsDbContext database,
    IAiRecipeProvider provider,
    IOptions<AiRecipeOptions> options,
    ILogger<AiRecipeDraftService> logger) : IAiRecipeDraftService
{
    public async Task<AiRecipeDraftResult> CreateDraftAsync(
        Guid householdId,
        AiRecipeDraftRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsOperational)
        {
            return Result(AiRecipeDraftStatus.NotConfigured);
        }

        if (!request.ExternalProcessingConfirmed
            || string.IsNullOrWhiteSpace(request.Prompt)
            || request.Prompt.Trim().Length > settings.MaxPromptCharacters
            || request.DesiredServings is < 1 or > 50
            || !HasValidValues(request.AvailableIngredients, 30, 120)
            || !HasValidValues(request.Preferences, 12, 80)
            || !HasValidValues(request.AvoidIngredients, 20, 120))
        {
            return Result(AiRecipeDraftStatus.InvalidRequest);
        }

        var reservation = AiRecipeDraftValidator.MaximumRequestCost(settings);
        if (reservation <= 0 || reservation > settings.MonthlyBudgetUsd)
        {
            return Result(AiRecipeDraftStatus.BudgetReached);
        }

        var providerInput = new AiProviderInput(
            request.Prompt.Trim(),
            request.DesiredServings,
            Normalize(request.AvailableIngredients),
            Normalize(request.Preferences),
            Normalize(request.AvoidIngredients));
        var conservativeInputBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(providerInput)) + 4_096;
        if (conservativeInputBytes > settings.MaxInputTokens)
        {
            return Result(AiRecipeDraftStatus.InvalidRequest);
        }

        var monthStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var reserved = await TryReserveAsync(
            householdId,
            monthStart,
            reservation,
            settings,
            cancellationToken);
        if (!reserved)
        {
            var usage = await database.AiUsageBudgets.AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.HouseholdId == householdId && entry.MonthStart == monthStart,
                    cancellationToken);
            return Result(usage?.RequestCount >= settings.MaxMonthlyRequestsPerHousehold
                ? AiRecipeDraftStatus.RequestLimitReached
                : AiRecipeDraftStatus.BudgetReached);
        }

        AiProviderResponse providerResponse;
        try
        {
            providerResponse = await provider.GenerateAsync(providerInput, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SettleAsync(householdId, monthStart, reservation, reservation, CancellationToken.None);
            throw;
        }

        if (providerResponse.Failure != AiProviderFailure.None)
        {
            await SettleAsync(householdId, monthStart, reservation, reservation, cancellationToken);
            return Result(providerResponse.Failure switch
            {
                AiProviderFailure.InvalidResponse => AiRecipeDraftStatus.InvalidProviderResponse,
                AiProviderFailure.RateLimited => AiRecipeDraftStatus.ProviderRateLimited,
                _ => AiRecipeDraftStatus.ProviderUnavailable
            });
        }

        var actualCost = AiRecipeDraftValidator.ActualCost(
            settings,
            providerResponse.PromptTokens,
            providerResponse.CompletionTokens);
        if (providerResponse.PromptTokens <= 0
            || providerResponse.CompletionTokens <= 0
            || providerResponse.PromptTokens > settings.MaxInputTokens
            || providerResponse.CompletionTokens > settings.MaxOutputTokens
            || actualCost < 0)
        {
            await SettleAsync(householdId, monthStart, reservation, reservation, cancellationToken);
            return Result(AiRecipeDraftStatus.InvalidProviderResponse);
        }

        await SettleAsync(householdId, monthStart, reservation, actualCost, cancellationToken);
        if (actualCost > reservation)
        {
            logger.LogError(
                "Configured AI cost reservation was exceeded for household {HouseholdId}.",
                householdId);
            return Result(AiRecipeDraftStatus.BudgetReached);
        }

        if (!string.IsNullOrWhiteSpace(providerResponse.Refusal))
        {
            return Result(AiRecipeDraftStatus.ProviderRefused);
        }

        var unitCodes = await database.Units.AsNoTracking()
            .Select(unit => unit.Code)
            .ToHashSetAsync(cancellationToken);
        if (!AiRecipeDraftValidator.TryParse(
                providerResponse.Content,
                unitCodes,
                request.DesiredServings,
                out var draft))
        {
            return Result(AiRecipeDraftStatus.InvalidProviderResponse);
        }

        return new AiRecipeDraftResult(draft, AiRecipeDraftStatus.Success);
    }

    private async Task<bool> TryReserveAsync(
        Guid householdId,
        DateOnly monthStart,
        decimal reservation,
        AiRecipeOptions settings,
        CancellationToken cancellationToken)
    {
        var affected = await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AiUsageBudgets"
                 ("HouseholdId", "MonthStart", "RequestCount", "SpentUsd", "ReservedUsd", "ReservationUpdatedAt")
             VALUES
                 ({householdId}, {monthStart}, 1, 0, {reservation}, NOW())
             ON CONFLICT ("HouseholdId", "MonthStart") DO UPDATE
             SET "RequestCount" = "AiUsageBudgets"."RequestCount" + 1,
                 "ReservedUsd" = CASE
                     WHEN "AiUsageBudgets"."ReservationUpdatedAt" IS NULL
                       OR "AiUsageBudgets"."ReservationUpdatedAt" < NOW() - INTERVAL '5 minutes'
                     THEN {reservation}
                     ELSE "AiUsageBudgets"."ReservedUsd" + {reservation}
                 END,
                 "ReservationUpdatedAt" = NOW()
             WHERE "AiUsageBudgets"."RequestCount" < {settings.MaxMonthlyRequestsPerHousehold}
               AND "AiUsageBudgets"."SpentUsd"
                   + CASE
                       WHEN "AiUsageBudgets"."ReservationUpdatedAt" IS NULL
                         OR "AiUsageBudgets"."ReservationUpdatedAt" < NOW() - INTERVAL '5 minutes'
                       THEN 0
                       ELSE "AiUsageBudgets"."ReservedUsd"
                     END
                   + {reservation}
                   <= {settings.MonthlyBudgetUsd}
             """,
            cancellationToken);
        return affected == 1;
    }

    private Task<int> SettleAsync(
        Guid householdId,
        DateOnly monthStart,
        decimal reservation,
        decimal actualCost,
        CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE "AiUsageBudgets"
             SET "ReservedUsd" = GREATEST("ReservedUsd" - {reservation}, 0),
                 "SpentUsd" = "SpentUsd" + {actualCost},
                 "ReservationUpdatedAt" = CASE
                     WHEN GREATEST("ReservedUsd" - {reservation}, 0) = 0 THEN NULL
                     ELSE NOW()
                 END
             WHERE "HouseholdId" = {householdId} AND "MonthStart" = {monthStart}
             """,
            cancellationToken);

    private static bool HasValidValues(string[]? values, int maxItems, int maxLength) =>
        values is null || (values.Length <= maxItems
            && values.All(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength));

    private static string[] Normalize(string[]? values) =>
        values?.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    private static AiRecipeDraftResult Result(AiRecipeDraftStatus status) => new(null, status);
}
