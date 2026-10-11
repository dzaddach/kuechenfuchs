namespace Kuechenfuchs.Api.Identity;

/// <summary>
/// Explicit binding of an external OIDC identity (issuer, subject) to an existing Küchenfuchs user and
/// exactly one deliberately selected household. Revoked links are kept for audit and never reactivated.
/// </summary>
public sealed class ExternalIdentityLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Earliest accepted token issue time (whole seconds). Tokens issued before this link existed – including
    /// tokens from an earlier, revoked link of the same identity – are never accepted for this link.
    /// </summary>
    public DateTimeOffset TokensNotBefore { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    /// <summary>When all Keycloak sessions (and thus refresh tokens) of this identity were ended after revocation.</summary>
    public DateTimeOffset? ProviderSessionsRevokedAt { get; set; }

    /// <summary>Last successful bearer request (coarse, at most one update per minute); server-side evidence of use.</summary>
    public DateTimeOffset? LastBearerAccessAt { get; set; }
}

/// <summary>
/// One-time linking attempt started from an authenticated PWA session. Only hashes of the state,
/// nonce and browser binding are stored; the PKCE verifier is protected with ASP.NET Data Protection.
/// </summary>
public sealed class ExternalIdentityLinkRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid HouseholdId { get; set; }
    public required string StateHash { get; set; }
    public required string NonceHash { get; set; }
    public required string BrowserBindingHash { get; set; }
    public required string ProtectedCodeVerifier { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public static class ExternalIdentityRevocationReasons
{
    public const string UserRequested = "user-requested";
}
