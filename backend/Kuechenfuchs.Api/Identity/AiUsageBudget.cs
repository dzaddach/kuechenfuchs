namespace Kuechenfuchs.Api.Identity;

public sealed class AiUsageBudget
{
    public Guid HouseholdId { get; set; }
    public DateOnly MonthStart { get; set; }
    public int RequestCount { get; set; }
    public decimal SpentUsd { get; set; }
    public decimal ReservedUsd { get; set; }
    public DateTimeOffset? ReservationUpdatedAt { get; set; }
}
