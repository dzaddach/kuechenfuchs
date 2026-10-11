using System.Text;
using System.Text.Json;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.Identity;
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
        var reservationId = await TryReserveAsync(
            householdId,
            monthStart,
            reservation,
            settings,
            cancellationToken);
        if (!reservationId.HasValue)
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
        catch
        {
            await SettleAsync(reservationId.Value, reservation, estimated: true, CancellationToken.None);
            throw;
        }

        if (providerResponse.Failure != AiProviderFailure.None)
        {
            var hasPlausibleUsage = providerResponse.PromptTokens > 0
                && providerResponse.CompletionTokens > 0
                && providerResponse.PromptTokens <= settings.MaxInputTokens
                && providerResponse.CompletionTokens <= settings.MaxOutputTokens;
            var reportedCost = hasPlausibleUsage
                ? AiRecipeDraftValidator.ActualCost(
                    settings,
                    providerResponse.PromptTokens,
                    providerResponse.CompletionTokens)
                : reservation;
            var failedCost = providerResponse.DefinitelyNoCharge
                ? 0
                : Math.Max(reservation, reportedCost);
            await SettleAsync(
                reservationId.Value,
                failedCost,
                estimated: !providerResponse.DefinitelyNoCharge && !hasPlausibleUsage,
                cancellationToken);
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
            || actualCost < 0
            || actualCost > reservation)
        {
            var hasPlausibleUsage = providerResponse.PromptTokens > 0
                && providerResponse.CompletionTokens > 0
                && actualCost >= 0;
            var conservativeCost = hasPlausibleUsage
                ? Math.Max(reservation, actualCost)
                : reservation;
            logger.LogError(
                "AI provider usage exceeded the configured token or cost reservation for household {HouseholdId}.",
                householdId);
            await SettleAsync(
                reservationId.Value,
                conservativeCost,
                estimated: !hasPlausibleUsage,
                cancellationToken);
            return Result(AiRecipeDraftStatus.InvalidProviderResponse);
        }

        await SettleAsync(reservationId.Value, actualCost, estimated: false, cancellationToken);

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

    private async Task<Guid?> TryReserveAsync(
        Guid householdId,
        DateOnly monthStart,
        decimal reservation,
        AiRecipeOptions settings,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AiUsageBudgets"
                 ("HouseholdId", "MonthStart", "RequestCount", "SpentUsd", "ReservedUsd")
             VALUES ({householdId}, {monthStart}, 0, 0, 0)
             ON CONFLICT ("HouseholdId", "MonthStart") DO NOTHING
             """,
            cancellationToken);

        var lockedBudgets = await database.AiUsageBudgets
            .FromSqlInterpolated(
                $"""SELECT * FROM "AiUsageBudgets" WHERE "HouseholdId" = {householdId} AND "MonthStart" = {monthStart} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        var budget = lockedBudgets.Single();
        var staleBefore = DateTimeOffset.UtcNow.AddSeconds(-settings.ReservationExpirationSeconds);
        var staleReservations = await database.AiUsageReservations
            .Where(item => item.HouseholdId == householdId
                && item.MonthStart == monthStart
                && item.FinalizedUsd == null
                && item.CreatedAt < staleBefore)
            .ToListAsync(cancellationToken);

        foreach (var stale in staleReservations)
        {
            ApplyFinalization(budget, stale, stale.ReservedUsd, estimated: true);
        }

        Guid? reservationId = null;
        if (budget.RequestCount < settings.MaxMonthlyRequestsPerHousehold
            && budget.SpentUsd + budget.ReservedUsd + reservation <= settings.MonthlyBudgetUsd)
        {
            reservationId = Guid.NewGuid();
            budget.RequestCount++;
            budget.ReservedUsd += reservation;
            database.AiUsageReservations.Add(new AiUsageReservation
            {
                Id = reservationId.Value,
                HouseholdId = householdId,
                MonthStart = monthStart,
                ReservedUsd = reservation,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reservationId;
    }

    private async Task SettleAsync(
        Guid reservationId,
        decimal finalCost,
        bool estimated,
        CancellationToken cancellationToken)
    {
        var key = await database.AiUsageReservations.AsNoTracking()
            .Where(item => item.Id == reservationId)
            .Select(item => new { item.HouseholdId, item.MonthStart })
            .SingleOrDefaultAsync(cancellationToken);
        if (key is null)
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var lockedBudgets = await database.AiUsageBudgets
            .FromSqlInterpolated(
                $"""SELECT * FROM "AiUsageBudgets" WHERE "HouseholdId" = {key.HouseholdId} AND "MonthStart" = {key.MonthStart} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        var budget = lockedBudgets.Single();
        await database.Entry(budget).ReloadAsync(cancellationToken);
        var lockedReservations = await database.AiUsageReservations
            .FromSqlInterpolated(
                $"""SELECT * FROM "AiUsageReservations" WHERE "Id" = {reservationId} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        var reservation = lockedReservations.SingleOrDefault();

        if (reservation is null)
        {
            return;
        }

        await database.Entry(reservation).ReloadAsync(cancellationToken);
        if (!reservation.FinalizedUsd.HasValue)
        {
            ApplyFinalization(budget, reservation, finalCost, estimated);
        }
        else if (reservation.IsEstimated && !estimated)
        {
            budget.SpentUsd += finalCost - reservation.FinalizedUsd.Value;
            reservation.FinalizedUsd = finalCost;
            reservation.IsEstimated = false;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ApplyFinalization(
        AiUsageBudget budget,
        AiUsageReservation reservation,
        decimal finalCost,
        bool estimated)
    {
        if (budget.ReservedUsd < reservation.ReservedUsd)
        {
            throw new InvalidOperationException("AI cost reservation totals are inconsistent.");
        }

        budget.ReservedUsd -= reservation.ReservedUsd;
        budget.SpentUsd += finalCost;
        reservation.FinalizedUsd = finalCost;
        reservation.IsEstimated = estimated;
    }

    private static bool HasValidValues(string[]? values, int maxItems, int maxLength) =>
        values is null || (values.Length <= maxItems
            && values.All(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength));

    private static string[] Normalize(string[]? values) =>
        values?.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    private static AiRecipeDraftResult Result(AiRecipeDraftStatus status) => new(null, status);
}
