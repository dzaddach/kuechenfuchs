using System.Security.Claims;
using Kuechenfuchs.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kuechenfuchs.Api.ExternalIdentity;

/// <summary>
/// Separate bearer authentication for future ChatGPT Actions endpoints. It is never the default scheme, so
/// cookie-protected PWA endpoints ignore bearer tokens and bearer endpoints ignore the PWA cookie.
/// </summary>
public static class ChatGptBearer
{
    public const string Scheme = "KeycloakBearer";
    public const string AuthenticatedPolicy = "ChatGptBearer";
    public const string RecipesReadPolicy = "ChatGptBearer.RecipesRead";
    public const string RecipesWritePolicy = "ChatGptBearer.RecipesWrite";
    public const string ScopeClaim = "scope";
    public const string HouseholdClaim = "kf_household_id";
    public const string LinkClaim = "kf_link_id";
    public const string ClientClaim = "client_id";
    public const string RecipesReadScope = "recipes:read";
    public const string RecipesWriteScope = "recipes:write";

    /// <summary>
    /// Runs after signature, issuer, audience and lifetime validation. Resolves the active link and the
    /// current user state from the database on every request, so revocation and blocking apply immediately.
    /// </summary>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ChatGptBearer).FullName!);
        if (context.SecurityToken is not JsonWebToken token)
        {
            context.Fail("Unsupported token format.");
            return;
        }

        var check = KeycloakTokenValidation.CheckAccessToken(token, options);
        if (!check.Succeeded)
        {
            logger.LogInformation(ExternalIdentityEvents.BearerRejected, "Bearer token rejected: {Reason}.", check.Failure);
            context.Fail("Token rejected.");
            return;
        }

        var database = services.GetRequiredService<KuechenfuchsDbContext>();
        var link = await database.ExternalIdentityLinks
            .AsNoTracking()
            .Where(item => item.Issuer == check.Issuer && item.Subject == check.Subject && item.RevokedAt == null)
            .Select(item => new
            {
                item.Id,
                item.UserId,
                item.HouseholdId,
                item.TokensNotBefore,
                item.User.IsEnabled,
                item.User.LockoutEnd,
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (link is null)
        {
            logger.LogInformation(ExternalIdentityEvents.BearerRejected, "Bearer token rejected: no active external identity link.");
            context.Fail("Token rejected.");
            return;
        }

        if (!KeycloakTokenValidation.IsIssuedForLink(check.IssuedAt, link.TokensNotBefore))
        {
            logger.LogInformation(ExternalIdentityEvents.BearerRejected, "Bearer token rejected: issued before external identity link {LinkId}.", link.Id);
            context.Fail("Token rejected.");
            return;
        }

        if (!link.IsEnabled || link.LockoutEnd > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(ExternalIdentityEvents.BearerRejected, "Bearer token rejected: user {UserId} is blocked.", link.UserId);
            context.Fail("Token rejected.");
            return;
        }

        var identity = new ClaimsIdentity(Scheme, ClaimTypes.NameIdentifier, null);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, link.UserId.ToString()));
        identity.AddClaim(new Claim(HouseholdClaim, link.HouseholdId.ToString()));
        identity.AddClaim(new Claim(LinkClaim, link.Id.ToString()));
        identity.AddClaim(new Claim(ClientClaim, check.ClientId));
        foreach (var scope in check.Scopes.Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(ScopeClaim, scope));
        }

        context.Principal = new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Per linked identity rate limit, then binding of every bearer request to the single household chosen
    /// during linking with a fresh membership check. Foreign, unbound or no longer accessible households are
    /// hidden with 404. Successful requests update a coarse last-access timestamp (server-side usage evidence).
    /// </summary>
    public static RouteGroupBuilder RequireBoundHousehold(this RouteGroupBuilder group) =>
        group.AddEndpointFilter((invocationContext, next) => AuthorizeHouseholdAsync(invocationContext, next, requireRouteMatch: true));

    /// <summary>
    /// Same checks for ChatGPT actions without a household route parameter: the household is taken exclusively
    /// from the verified link, never from client input, and exposed via <see cref="GetLinkedHousehold"/>.
    /// </summary>
    public static RouteGroupBuilder RequireLinkedHousehold(this RouteGroupBuilder group) =>
        group.AddEndpointFilter((invocationContext, next) => AuthorizeHouseholdAsync(invocationContext, next, requireRouteMatch: false));

    /// <summary>Household, user and link of the current bearer request after <see cref="RequireLinkedHousehold"/>.</summary>
    public static LinkedHousehold GetLinkedHousehold(this HttpContext context) =>
        context.Items[typeof(LinkedHousehold)] as LinkedHousehold
        ?? throw new InvalidOperationException("RequireLinkedHousehold must run before this endpoint.");

    private static async ValueTask<object?> AuthorizeHouseholdAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next,
        bool requireRouteMatch)
    {
        var context = invocationContext.HttpContext;
        var user = context.User;
        if (!Guid.TryParse(user.FindFirstValue(LinkClaim), out var linkId)
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(user.FindFirstValue(HouseholdClaim), out var householdId))
        {
            return Results.NotFound();
        }

        using var lease = context.RequestServices.GetRequiredService<ChatGptBearerRateLimiter>().AttemptAcquire(linkId);
        if (!lease.IsAcquired)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ChatGptBearer).FullName!)
                .LogWarning(ExternalIdentityEvents.BearerRateLimited, "Bearer rate limit reached for link {LinkId}.", linkId);
            context.Response.Headers.RetryAfter = "60";
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Request rate limit reached");
        }

        if (requireRouteMatch
            && (!Guid.TryParse(context.Request.RouteValues["householdId"]?.ToString(), out var routeHouseholdId)
                || routeHouseholdId != householdId))
        {
            return Results.NotFound();
        }

        var database = context.RequestServices.GetRequiredService<KuechenfuchsDbContext>();
        var isMember = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId && member.UserId == userId,
            context.RequestAborted);
        if (!isMember)
        {
            return Results.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var staleBefore = now.AddMinutes(-1);
        await database.ExternalIdentityLinks
            .Where(link => link.Id == linkId && (link.LastBearerAccessAt == null || link.LastBearerAccessAt < staleBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.LastBearerAccessAt, now), context.RequestAborted);
        context.Items[typeof(LinkedHousehold)] = new LinkedHousehold(householdId, userId, linkId);
        context.Response.Headers.CacheControl = "no-store";
        return await next(invocationContext);
    }
}

public sealed record LinkedHousehold(Guid HouseholdId, Guid UserId, Guid LinkId);

/// <summary>Fixed-window limiter per linked identity for bearer endpoints (runs after authentication).</summary>
public sealed class ChatGptBearerRateLimiter(IOptions<KeycloakOptions> options) : IDisposable
{
    private readonly System.Threading.RateLimiting.PartitionedRateLimiter<Guid> _limiter =
        System.Threading.RateLimiting.PartitionedRateLimiter.Create<Guid, Guid>(linkId =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                linkId,
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.Value.BearerRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

    public System.Threading.RateLimiting.RateLimitLease AttemptAcquire(Guid linkId) => _limiter.AttemptAcquire(linkId);

    public void Dispose() => _limiter.Dispose();
}
