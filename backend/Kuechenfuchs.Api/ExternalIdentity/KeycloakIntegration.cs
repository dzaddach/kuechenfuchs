using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kuechenfuchs.Api.ExternalIdentity;

public sealed record StartExternalIdentityLinkRequest(Guid HouseholdId);

public sealed record StartExternalIdentityLinkResponse(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record ChatGptAuthorizationCheckResponse(string Status, Guid HouseholdId, IReadOnlyList<string> Scopes);

public static class KeycloakIntegration
{
    private const string CallbackPath = "/api/v1/integrations/keycloak/callback";

    /// <summary>Registers Keycloak linking and bearer authentication when Keycloak:Enabled is true.</summary>
    public static bool AddKeycloakIntegration(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(KeycloakOptions.SectionName);
        var options = section.Get<KeycloakOptions>() ?? new KeycloakOptions();
        if (!options.Enabled)
        {
            return false;
        }

        options.Validate(builder.Environment.IsDevelopment());
        builder.Services.Configure<KeycloakOptions>(section);
        builder.Services.AddHttpClient(KeycloakOidcClient.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(options.BackchannelTimeoutSeconds);
                client.MaxResponseContentBufferSize = 256 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(services =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                options.MetadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(
                    services.GetRequiredService<IHttpClientFactory>().CreateClient(KeycloakOidcClient.HttpClientName))
                {
                    RequireHttps = options.RequireHttpsMetadata,
                }));
        builder.Services.AddScoped<KeycloakOidcClient>();
        builder.Services.AddSingleton<KeycloakAdminClient>();
        builder.Services.AddSingleton<ChatGptBearerRateLimiter>();
        builder.Services.AddScoped<ExternalIdentityLinkService>();

        builder.Services.AddAuthentication()
            .AddJwtBearer(ChatGptBearer.Scheme, jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.IncludeErrorDetails = false;
                jwt.SaveToken = false;
                jwt.TokenValidationParameters = KeycloakTokenValidation.CreateAccessTokenParameters(options);
                jwt.Events = new() { OnTokenValidated = ChatGptBearer.OnTokenValidatedAsync };
            });
        builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(ChatGptBearer.Scheme)
            .Configure<IConfigurationManager<OpenIdConnectConfiguration>>((jwt, configurationManager) =>
                jwt.ConfigurationManager = configurationManager);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ChatGptBearer.AuthenticatedPolicy, policy => policy
                .AddAuthenticationSchemes(ChatGptBearer.Scheme)
                .RequireAuthenticatedUser())
            .AddPolicy(ChatGptBearer.RecipesReadPolicy, policy => policy
                .AddAuthenticationSchemes(ChatGptBearer.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ChatGptBearer.ScopeClaim, ChatGptBearer.RecipesReadScope))
            .AddPolicy(ChatGptBearer.RecipesWritePolicy, policy => policy
                .AddAuthenticationSchemes(ChatGptBearer.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ChatGptBearer.ScopeClaim, ChatGptBearer.RecipesWriteScope));
        return true;
    }

    /// <summary>Cookie/CSRF-protected linking endpoints below the existing /api/v1 PWA group.</summary>
    public static RouteGroupBuilder MapExternalIdentityLinkEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/integrations/keycloak").WithTags("Keycloak linking");
        group.MapPost("/link-requests", StartLinkAsync)
            .WithSummary("Start linking a Keycloak identity to the signed-in user for one household");
        group.MapGet("/callback", CompleteLinkAsync)
            .AllowAnonymous()
            .WithSummary("OIDC redirect target of the one-time linking flow");
        group.MapGet("/links", ListLinksAsync)
            .WithSummary("List the signed-in user's external identity links");
        group.MapDelete("/links/{linkId:guid}", RevokeLinkAsync)
            .WithSummary("Revoke an external identity link");
        return group;
    }

    /// <summary>Bearer-only endpoints for ChatGPT Actions. Deliberately outside the cookie-protected /api/v1 group.</summary>
    public static RouteGroupBuilder MapChatGptBearerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/integrations/chatgpt")
            .RequireAuthorization(ChatGptBearer.AuthenticatedPolicy)
            .RequireBoundHousehold()
            .WithTags("ChatGPT bearer");
        group.MapGet(
                "/households/{householdId:guid}/authorization-check",
                (Guid householdId, ClaimsPrincipal principal, HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    var scopes = principal.FindAll(ChatGptBearer.ScopeClaim).Select(claim => claim.Value).ToList();
                    return Results.Ok(new ChatGptAuthorizationCheckResponse("authorized", householdId, scopes));
                })
            .RequireAuthorization(ChatGptBearer.RecipesReadPolicy)
            .WithSummary("Diagnostic bearer check without personal data (P5a)");
        return group;
    }

    private static async Task<IResult> StartLinkAsync(
        StartExternalIdentityLinkRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IAntiforgery antiforgery,
        IHostEnvironment environment,
        ExternalIdentityLinkService linkService,
        ILogger<ExternalIdentityLinkService> logger,
        CancellationToken cancellationToken)
    {
        if (!await HasValidAntiforgeryToken(context, antiforgery))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "CSRF validation failed");
        }

        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        LinkStartResult? result;
        try
        {
            result = await linkService.StartAsync(userId, request.HouseholdId, cancellationToken);
        }
        catch (Exception exception) when (IsKeycloakUnavailable(exception, cancellationToken))
        {
            logger.LogWarning("Keycloak discovery is unavailable: {ExceptionType}.", exception.GetType().Name);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Identity provider unavailable");
        }

        if (result is null)
        {
            return Results.NotFound();
        }

        var options = context.RequestServices.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        context.Response.Cookies.Append(BindingCookieName(environment), result.BrowserBinding, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            // Lax: the cookie must accompany the top-level redirect back from Keycloak.
            SameSite = SameSiteMode.Lax,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            Path = CallbackPath,
            MaxAge = TimeSpan.FromSeconds(options.LinkRequestLifetimeSeconds),
        });
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new StartExternalIdentityLinkResponse(result.AuthorizationUrl, result.ExpiresAt));
    }

    private static async Task<IResult> CompleteLinkAsync(
        HttpContext context,
        IHostEnvironment environment,
        ExternalIdentityLinkService linkService,
        ILogger<ExternalIdentityLinkService> logger,
        CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var cookieName = BindingCookieName(environment);
        var sessionUserId = TryGetUserId(context.User, out var userId) ? userId : (Guid?)null;
        LinkCompletionStatus status;
        try
        {
            status = await linkService.CompleteAsync(
                query["state"].ToString(),
                query["code"].ToString(),
                query["error"].ToString(),
                context.Request.Cookies[cookieName],
                sessionUserId,
                cancellationToken);
        }
        catch (Exception exception) when (IsKeycloakUnavailable(exception, cancellationToken))
        {
            logger.LogWarning("Keycloak is unavailable during link completion: {ExceptionType}.", exception.GetType().Name);
            status = LinkCompletionStatus.Failed;
        }

        context.Response.Cookies.Delete(cookieName, new CookieOptions
        {
            Path = CallbackPath,
            Secure = !environment.IsDevelopment() || context.Request.IsHttps,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Redirect($"/settings/chatgpt?keycloakLink={StatusParameter(status)}");
    }

    private static async Task<IResult> ListLinksAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        ExternalIdentityLinkService linkService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await linkService.ListAsync(userId, cancellationToken));
    }

    private static async Task<IResult> RevokeLinkAsync(
        Guid linkId,
        ClaimsPrincipal principal,
        HttpContext context,
        IAntiforgery antiforgery,
        ExternalIdentityLinkService linkService,
        CancellationToken cancellationToken)
    {
        if (!await HasValidAntiforgeryToken(context, antiforgery))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "CSRF validation failed");
        }

        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await linkService.RevokeAsync(userId, linkId, cancellationToken);
        context.Response.Headers.CacheControl = "no-store";
        return result.Found
            ? Results.Ok(new { revoked = true, providerSessionsEnded = result.ProviderSessionsEnded })
            : Results.NotFound();
    }

    private static string StatusParameter(LinkCompletionStatus status) => status switch
    {
        LinkCompletionStatus.Linked => "linked",
        LinkCompletionStatus.Conflict => "conflict",
        LinkCompletionStatus.Expired => "expired",
        LinkCompletionStatus.Cancelled => "cancelled",
        LinkCompletionStatus.ProviderUnavailable => "unavailable",
        _ => "failed",
    };

    private static string BindingCookieName(IHostEnvironment environment) =>
        environment.IsDevelopment() ? "KF.KeycloakLink" : "__Secure-KF.KeycloakLink";

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool IsKeycloakUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is KeycloakUnavailableException or HttpRequestException or InvalidOperationException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<bool> HasValidAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
