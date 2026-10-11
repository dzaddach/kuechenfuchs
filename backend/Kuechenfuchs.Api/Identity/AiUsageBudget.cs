namespace Kuechenfuchs.Api.Identity;

public sealed class AiUsageBudget
{
    public Guid HouseholdId { get; set; }
    public DateOnly MonthStart { get; set; }
    public int RequestCount { get; set; }
    public decimal SpentUsd { get; set; }
    public decimal ReservedUsd { get; set; }
}

public sealed class AiUsageReservation
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public DateOnly MonthStart { get; set; }
    public decimal ReservedUsd { get; set; }
    public decimal? FinalizedUsd { get; set; }
    public bool IsEstimated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
