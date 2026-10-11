using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Kuechenfuchs.Api.Ai;
using Kuechenfuchs.Api.Data;
using Kuechenfuchs.Api.ExternalIdentity;
using Kuechenfuchs.Api.Identity;
using Kuechenfuchs.Api.Recipes;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    try
    {
        using var response = await client.GetAsync("http://127.0.0.1:8080/api/health/ready");
        Environment.Exit(response.IsSuccessStatusCode ? 0 : 1);
        return;
    }
    catch (HttpRequestException)
    {
        Environment.Exit(1);
        return;
    }
    catch (TaskCanceledException)
    {
        Environment.Exit(1);
        return;
    }
}

var builder = WebApplication.CreateBuilder(args);
// Die Request-Protokollierung von ASP.NET schreibt vollständige URLs inklusive Query (z. B. OAuth-"code"
// und "state" im Keycloak-Callback). Diese Einträge dürfen nie in normalen Logs landen.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var databaseSettings = builder.Configuration.GetSection("Database");
var connectionString = new NpgsqlConnectionStringBuilder
{
    Host = databaseSettings["Host"] ?? "localhost",
    Port = databaseSettings.GetValue("Port", 5432),
    Database = databaseSettings["Name"] ?? "kuechenfuchs",
    Username = databaseSettings["User"] ?? "kuechenfuchs",
    Password = databaseSettings["Password"] ?? string.Empty,
    GssEncryptionMode = databaseSettings.GetValue("GssEncryptionMode", GssEncryptionMode.Disable),
}.ConnectionString;

builder.Services.AddDbContext<KuechenfuchsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddOpenApi("v1");
builder.Services.Configure<AiRecipeOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient<OpenAiRecipeProvider>(client =>
{
    var timeout = builder.Configuration.GetValue("Ai:TimeoutSeconds", 30);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeout, 1, 120));
    client.MaxResponseContentBufferSize = 256 * 1024;
});
builder.Services.AddScoped<IAiRecipeProvider>(services => services.GetRequiredService<OpenAiRecipeProvider>());
builder.Services.AddScoped<IAiRecipeDraftService, AiRecipeDraftService>();
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 14;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<KuechenfuchsDbContext>()
    .AddDefaultTokenProviders();

var cookieLifetimeSeconds = builder.Configuration.GetValue("Authentication:CookieLifetimeSeconds", 28_800);
if (cookieLifetimeSeconds < 1)
{
    throw new InvalidOperationException("Authentication:CookieLifetimeSeconds must be at least 1.");
}

var isDevelopment = builder.Environment.IsDevelopment();
var securePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = isDevelopment ? "KF.Identity" : "__Host-KF.Identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = securePolicy;
    options.ExpireTimeSpan = TimeSpan.FromSeconds(cookieLifetimeSeconds);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-KF-CSRF";
    options.Cookie.Name = isDevelopment ? "KF.Antiforgery" : "__Host-KF.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = securePolicy;
});

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}

// Hinter TLS-terminierenden Proxys (Heimserver: Edge-Caddy -> Küchenfuchs-Caddy -> API) nur Schema und
// Client-IP aus privaten Compose-Netzen übernehmen; Host-Header und daraus abgeleitete URLs nie.
var trustForwardedHeaders = builder.Configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false);
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 2;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8" })
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    });
}

builder.Services.AddAuthorization();
var keycloakEnabled = builder.AddKeycloakIntegration();
builder.Services.AddRateLimiter(options =>
{
    var aiRequestsPerMinute = builder.Configuration.GetValue("Ai:RequestsPerMinutePerUser", 8);
    if (aiRequestsPerMinute < 1)
    {
        throw new InvalidOperationException("Ai:RequestsPerMinutePerUser must be at least 1.");
    }

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Request rate limit reached"
            },
            cancellationToken);
    };
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddPolicy("ai-draft", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiRequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    // Kontrollierter, einmaliger Migrationsschritt für Production (z. B. Compose-Dienst "migrate").
    await DatabaseBootstrapper.InitializeAsync(app.Services, app.Configuration, applyMigrations: true);
    return;
}

if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    await DatabaseBootstrapper.InitializeAsync(
        app.Services,
        app.Configuration,
        applyMigrations: false,
        requireInitialAdmin: true);
    return;
}

if (app.Environment.IsDevelopment())
{
    await DatabaseBootstrapper.InitializeAsync(app.Services, app.Configuration, applyMigrations: true);
    app.MapOpenApi("/api/openapi/{documentName}.json");
}

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var isWriteMethod = HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method);
    if (isWriteMethod && context.User.Identity?.IsAuthenticated == true)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await HasValidAntiforgeryToken(context, antiforgery))
        {
            await AntiforgeryProblem().ExecuteAsync(context);
            return;
        }
    }

    await next(context);
});
app.UseAuthorization();

app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));

app.MapGet(
    "/api/health/ready",
    async Task<IResult> (KuechenfuchsDbContext database, ILogger<Program> logger) =>
    {
        if (!await database.Database.CanConnectAsync())
        {
            logger.LogWarning("Readiness check failed because PostgreSQL is unavailable.");
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Database unavailable");
        }

        return Results.Ok(new { status = "ready", database = "connected" });
    });

var auth = app.MapGroup("/api/auth");

auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
});

auth.MapPost(
        "/login",
        async Task<IResult> (
            LoginRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            KuechenfuchsDbContext database) =>
        {
            if (!await HasValidAntiforgeryToken(context, antiforgery))
            {
                return AntiforgeryProblem();
            }

            if (string.IsNullOrWhiteSpace(request.Email)
                || !new EmailAddressAttribute().IsValid(request.Email)
                || string.IsNullOrEmpty(request.Password)
                || request.Password.Length > 256)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid sign-in request");
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());
            if (user is null || !user.IsEnabled)
            {
                return InvalidCredentials();
            }

            var result = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                isPersistent: false,
                lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return InvalidCredentials();
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await CreateSession(user, database));
        })
    .AllowAnonymous()
    .RequireRateLimiting("login");

auth.MapGet(
        "/session",
        async Task<IResult> (ClaimsPrincipal principal, KuechenfuchsDbContext database) =>
        {
            var userId = GetUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId);
            if (user is null || !user.IsEnabled)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await CreateSession(user, database));
        })
    .RequireAuthorization();

auth.MapPost(
        "/logout",
        async Task<IResult> (HttpContext context, IAntiforgery antiforgery, SignInManager<ApplicationUser> signInManager) =>
        {
            if (!await HasValidAntiforgeryToken(context, antiforgery))
            {
                return AntiforgeryProblem();
            }

            await signInManager.SignOutAsync();
            context.Response.Headers.CacheControl = "no-store";
            return Results.NoContent();
        })
    .RequireAuthorization();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapRecipeEndpoints();
api.MapAiRecipeEndpoints();
if (keycloakEnabled)
{
    api.MapExternalIdentityLinkEndpoints();
    app.MapChatGptBearerEndpoints();
}

api.MapGet(
    "/me",
    async Task<IResult> (ClaimsPrincipal principal, KuechenfuchsDbContext database) =>
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId);
        if (user is null || !user.IsEnabled)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await CreateSession(user, database));
    });

api.MapGet(
    "/households/{householdId:guid}",
    async Task<IResult> (Guid householdId, ClaimsPrincipal principal, KuechenfuchsDbContext database) =>
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var household = await database.HouseholdMembers
            .AsNoTracking()
            .Where(member => member.HouseholdId == householdId && member.UserId == userId)
            .Select(member => new { member.Household.Id, member.Household.Name, member.Role })
            .SingleOrDefaultAsync();
        return household is null ? Results.NotFound() : Results.Ok(household);
    });

api.MapGet(
    "/households/{householdId:guid}/members",
    async Task<IResult> (Guid householdId, ClaimsPrincipal principal, KuechenfuchsDbContext database) =>
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var isMember = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId && member.UserId == userId);
        if (!isMember)
        {
            return Results.NotFound();
        }

        var members = await database.HouseholdMembers
            .AsNoTracking()
            .Where(member => member.HouseholdId == householdId)
            .OrderBy(member => member.JoinedAt)
            .Select(member => new
            {
                member.UserId,
                member.User.Email,
                member.Role,
                member.JoinedAt,
            })
            .ToListAsync();
        return Results.Ok(members);
    });

api.MapPost(
    "/households/{householdId:guid}/members",
    async Task<IResult> (
        Guid householdId,
        AddHouseholdMemberRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        KuechenfuchsDbContext database) =>
    {
        if (!await HasValidAntiforgeryToken(context, antiforgery))
        {
            return AntiforgeryProblem();
        }

        var userId = GetUserId(principal);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var isOwner = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId
                && member.UserId == userId
                && member.Role == HouseholdRoles.Owner);
        if (!isOwner)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !new EmailAddressAttribute().IsValid(request.Email))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid member request");
        }

        var invitedUser = await userManager.FindByEmailAsync(request.Email.Trim());
        if (invitedUser is null || !invitedUser.IsEnabled)
        {
            return Results.NotFound();
        }

        var alreadyMember = await database.HouseholdMembers.AnyAsync(
            member => member.HouseholdId == householdId && member.UserId == invitedUser.Id);
        if (alreadyMember)
        {
            return Results.Conflict();
        }

        var membership = new HouseholdMember
        {
            HouseholdId = householdId,
            UserId = invitedUser.Id,
            Role = HouseholdRoles.Member,
        };
        database.HouseholdMembers.Add(membership);
        await database.SaveChangesAsync();
        context.Response.Headers.CacheControl = "no-store";
        return Results.Created(
            $"/api/v1/households/{householdId}/members/{invitedUser.Id}",
            new { membership.HouseholdId, membership.UserId, membership.Role, membership.JoinedAt });
    });

await app.RunAsync();

static Guid? GetUserId(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

static async Task<object> CreateSession(ApplicationUser user, KuechenfuchsDbContext database)
{
    var households = await database.HouseholdMembers
        .AsNoTracking()
        .Where(member => member.UserId == user.Id)
        .OrderBy(member => member.Household.Name)
        .Select(member => new { member.HouseholdId, member.Household.Name, member.Role })
        .ToListAsync();
    return new { userId = user.Id, email = user.Email, households };
}

static async Task<bool> HasValidAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
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

static IResult AntiforgeryProblem() =>
    Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "CSRF validation failed");

static IResult InvalidCredentials() =>
    Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password");

public sealed record LoginRequest(string Email, string Password);
public sealed record AddHouseholdMemberRequest(string Email);

public partial class Program;
