namespace Kuechenfuchs.Api.Identity;

public sealed class HouseholdMember
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
