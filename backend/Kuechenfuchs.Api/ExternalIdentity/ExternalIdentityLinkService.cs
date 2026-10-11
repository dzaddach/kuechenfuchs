using System.Security.Cryptography;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kuechenfuchs.Api.ExternalIdentity;

public enum LinkCompletionStatus
{
    Linked,
    Conflict,
    Expired,
    Cancelled,
    Failed,
}

public sealed record LinkStartResult(string AuthorizationUrl, string BrowserBinding, DateTimeOffset ExpiresAt);

public sealed record ExternalIdentityLinkResponse(
    Guid Id,
    string Issuer,
    Guid HouseholdId,
    string HouseholdName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

/// <summary>
/// Explicit, one-time linking of a Keycloak identity to the signed-in Küchenfuchs user. Linking never uses
/// e-mail addresses; only the verified (issuer, subject) of a fresh Keycloak login is accepted.
/// </summary>
public sealed class ExternalIdentityLinkService(
    KuechenfuchsDbContext database,
    KeycloakOidcClient oidcClient,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<KeycloakOptions> options,
    ILogger<ExternalIdentityLinkService> logger)
{
    private const int MaxStateLength = 512;
    private const int MaxCodeLength = 4_096;
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("Kuechenfuchs.ExternalIdentity.LinkRequest.CodeVerifier.v1");

    /// <summary>Starts a link request for a household the user currently belongs to; returns null otherwise.</summary>
    public async Task<LinkStartResult?> StartAsync(Guid userId, Guid householdId, CancellationToken cancellationToken)
    {
        var isActiveMember = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId && member.UserId == userId && member.User.IsEnabled,
            cancellationToken);
        if (!isActiveMember)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var staleBefore = now.AddDays(-1);
        // Only one pending request per user; older attempts become unusable immediately.
        await database.ExternalIdentityLinkRequests
            .Where(request => request.UserId == userId || request.ExpiresAt < staleBefore)
            .ExecuteDeleteAsync(cancellationToken);

        var state = LinkSecrets.CreateRandomValue();
        var nonce = LinkSecrets.CreateRandomValue();
        var browserBinding = LinkSecrets.CreateRandomValue();
        var codeVerifier = LinkSecrets.CreateRandomValue();
        var expiresAt = now.AddSeconds(options.Value.LinkRequestLifetimeSeconds);

        var authorizationUrl = await oidcClient.BuildAuthorizationUrlAsync(
            state,
            nonce,
            LinkSecrets.CreateS256CodeChallenge(codeVerifier),
            cancellationToken);

        database.ExternalIdentityLinkRequests.Add(new ExternalIdentityLinkRequest
        {
            UserId = userId,
            HouseholdId = householdId,
            StateHash = LinkSecrets.Hash(state),
            NonceHash = LinkSecrets.Hash(nonce),
            BrowserBindingHash = LinkSecrets.Hash(browserBinding),
            ProtectedCodeVerifier = _protector.Protect(codeVerifier),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await database.SaveChangesAsync(cancellationToken);
        return new LinkStartResult(authorizationUrl, browserBinding, expiresAt);
    }

    /// <summary>
    /// Completes a link request exactly once. The request is consumed before any further check, so a
    /// manipulated, replayed or failed callback can never be retried with the same state.
    /// </summary>
    public async Task<LinkCompletionStatus> CompleteAsync(
        string? state,
        string? code,
        string? error,
        string? browserBinding,
        Guid? sessionUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state) || state.Length > MaxStateLength)
        {
            return LinkCompletionStatus.Failed;
        }

        var stateHash = LinkSecrets.Hash(state);
        var request = await database.ExternalIdentityLinkRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.StateHash == stateHash, cancellationToken);
        if (request is null)
        {
            logger.LogWarning("Link callback with unknown state rejected.");
            return LinkCompletionStatus.Failed;
        }

        var now = DateTimeOffset.UtcNow;
        var consumed = await database.ExternalIdentityLinkRequests
            .Where(item => item.Id == request.Id && item.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        if (consumed != 1)
        {
            logger.LogWarning("Replayed link request {LinkRequestId} rejected.", request.Id);
            return LinkCompletionStatus.Failed;
        }

        if (request.ExpiresAt <= now)
        {
            return LinkCompletionStatus.Expired;
        }

        if (!LinkSecrets.HashMatches(browserBinding, request.BrowserBindingHash)
            || (sessionUserId is not null && sessionUserId != request.UserId))
        {
            logger.LogWarning("Link request {LinkRequestId} rejected: browser binding or session mismatch.", request.Id);
            return LinkCompletionStatus.Failed;
        }

        if (!string.IsNullOrEmpty(error))
        {
            return LinkCompletionStatus.Cancelled;
        }

        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
        {
            return LinkCompletionStatus.Failed;
        }

        string codeVerifier;
        try
        {
            codeVerifier = _protector.Unprotect(request.ProtectedCodeVerifier);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Link request {LinkRequestId} has an unreadable PKCE verifier.", request.Id);
            return LinkCompletionStatus.Failed;
        }

        var identity = await oidcClient.RedeemCodeAsync(code, codeVerifier, request.NonceHash, cancellationToken);
        if (identity is null)
        {
            return LinkCompletionStatus.Failed;
        }

        return await CreateLinkAsync(request, identity, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalIdentityLinkResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await database.ExternalIdentityLinks
            .AsNoTracking()
            .Where(link => link.UserId == userId)
            .OrderByDescending(link => link.CreatedAt)
            .Select(link => new ExternalIdentityLinkResponse(
                link.Id,
                link.Issuer,
                link.HouseholdId,
                link.Household.Name,
                link.CreatedAt,
                link.RevokedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Revokes one of the user's own links. Bearer access stops with the next request.</summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid linkId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var revoked = await database.ExternalIdentityLinks
            .Where(link => link.Id == linkId && link.UserId == userId && link.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.RevokedAt, now)
                    .SetProperty(link => link.RevokedReason, ExternalIdentityRevocationReasons.UserRequested),
                cancellationToken);
        if (revoked == 1)
        {
            logger.LogInformation("External identity link {LinkId} revoked by user {UserId}.", linkId, userId);
            return true;
        }

        return await database.ExternalIdentityLinks.AnyAsync(
            link => link.Id == linkId && link.UserId == userId,
            cancellationToken);
    }

    private async Task<LinkCompletionStatus> CreateLinkAsync(
        ExternalIdentityLinkRequest request,
        VerifiedExternalIdentity identity,
        CancellationToken cancellationToken)
    {
        var isActiveMember = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == request.HouseholdId
                && member.UserId == request.UserId
                && member.User.IsEnabled,
            cancellationToken);
        if (!isActiveMember)
        {
            logger.LogWarning("Link request {LinkRequestId} rejected: user or membership no longer active.", request.Id);
            return LinkCompletionStatus.Failed;
        }

        var hasConflict = await database.ExternalIdentityLinks.AnyAsync(
            link => link.RevokedAt == null
                && ((link.Issuer == identity.Issuer && link.Subject == identity.Subject)
                    || (link.UserId == request.UserId && link.Issuer == identity.Issuer)),
            cancellationToken);
        if (hasConflict)
        {
            logger.LogWarning("Link request {LinkRequestId} rejected: identity or user already linked.", request.Id);
            return LinkCompletionStatus.Conflict;
        }

        var createdAt = DateTimeOffset.UtcNow;
        var link = new ExternalIdentityLink
        {
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            UserId = request.UserId,
            HouseholdId = request.HouseholdId,
            CreatedAt = createdAt,
            TokensNotBefore = KeycloakTokenValidation.CalculateTokensNotBefore(
                createdAt,
                options.Value.ClockSkewSeconds),
        };
        database.ExternalIdentityLinks.Add(link);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogWarning("Link request {LinkRequestId} rejected: concurrent link for the same identity.", request.Id);
            return LinkCompletionStatus.Conflict;
        }

        logger.LogInformation(
            "External identity link {LinkId} created for user {UserId} and household {HouseholdId}.",
            link.Id,
            link.UserId,
            link.HouseholdId);
        return LinkCompletionStatus.Linked;
    }
}
