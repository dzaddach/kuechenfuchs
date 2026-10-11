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
            logger.LogInformation("Bearer token rejected: {Reason}.", check.Failure);
            context.Fail("Token rejected.");
            return;
        }

        var database = services.GetRequiredService<KuechenfuchsDbContext>();
        var link = await database.ExternalIdentityLinks
            .AsNoTracking()
            .Where(item => item.Issuer == check.Issuer && item.Subject == check.Subject && item.RevokedAt == null)
            .Select(item => new { item.Id, item.UserId, item.HouseholdId, item.User.IsEnabled, item.User.LockoutEnd })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (link is null)
        {
            logger.LogInformation("Bearer token rejected: no active external identity link.");
            context.Fail("Token rejected.");
            return;
        }

        if (!link.IsEnabled || link.LockoutEnd > DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Bearer token rejected: user {UserId} is blocked.", link.UserId);
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
    /// Binds every bearer request to the single household chosen during linking and re-checks the current
    /// membership. Foreign, unbound or no longer accessible households are hidden with 404.
    /// </summary>
    public static RouteGroupBuilder RequireBoundHousehold(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (invocationContext, next) =>
        {
            var context = invocationContext.HttpContext;
            var user = context.User;
            if (!Guid.TryParse(context.Request.RouteValues["householdId"]?.ToString(), out var householdId)
                || !Guid.TryParse(user.FindFirstValue(HouseholdClaim), out var boundHouseholdId)
                || boundHouseholdId != householdId
                || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Results.NotFound();
            }

            var database = context.RequestServices.GetRequiredService<KuechenfuchsDbContext>();
            var isMember = await database.HouseholdMembers.AnyAsync(
                member => member.HouseholdId == householdId && member.UserId == userId,
                context.RequestAborted);
            return isMember ? await next(invocationContext) : Results.NotFound();
        });
}
