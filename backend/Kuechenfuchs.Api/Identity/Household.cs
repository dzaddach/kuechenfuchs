namespace Kuechenfuchs.Api.Identity;

public sealed class Household
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HouseholdMember> Members { get; } = new List<HouseholdMember>();
}
