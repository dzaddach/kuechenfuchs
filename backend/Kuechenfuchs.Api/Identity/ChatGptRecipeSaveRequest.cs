namespace Kuechenfuchs.Api.Identity;

/// <summary>
/// A validated recipe that ChatGPT proposed to save. Nothing is stored as a recipe until the request is
/// explicitly confirmed in a separate call; confirmation is single-use, expires and is bound to the link.
/// </summary>
public sealed class ChatGptRecipeSaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LinkId { get; set; }
    public Guid UserId { get; set; }
    public Guid HouseholdId { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? RecipeId { get; set; }
}
