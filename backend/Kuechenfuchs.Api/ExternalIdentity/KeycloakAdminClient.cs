using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kuechenfuchs.Api.ExternalIdentity;

public sealed record ChatGptSessionStatus(bool Active, DateTimeOffset? LastAccessAt);

/// <summary>
/// Narrow use of the Keycloak Admin REST API via the internal network with a service account
/// (realm-management: view-users, manage-users): end a user's sessions – which invalidates all refresh
/// tokens issued in them – and read whether the ChatGPT client currently holds a session.
/// </summary>
public sealed class KeycloakAdminClient(
    IHttpClientFactory httpClientFactory,
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    IOptions<KeycloakOptions> options,
    ILogger<KeycloakAdminClient> logger)
{
    private readonly KeycloakOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    /// <summary>Ends all Keycloak sessions of the user. True when no session remains (including unknown users).</summary>
    public async Task<bool> EndUserSessionsAsync(string subject, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var userId))
        {
            logger.LogWarning(ExternalIdentityEvents.ProviderSessionsFailed, "Keycloak subject is not a user ID; sessions cannot be ended.");
            return false;
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Post, $"users/{userId}/logout", cancellationToken);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
            {
                logger.LogInformation(ExternalIdentityEvents.ProviderSessionsEnded, "Keycloak sessions ended for an external identity.");
                return true;
            }

            logger.LogWarning(
                ExternalIdentityEvents.ProviderSessionsFailed,
                "Keycloak refused to end sessions with HTTP {StatusCode}.",
                (int)response.StatusCode);
            return false;
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            logger.LogWarning(ExternalIdentityEvents.ProviderSessionsFailed, "Keycloak is unavailable: {ExceptionType}.", exception.GetType().Name);
            return false;
        }
    }

    /// <summary>Session state of the ChatGPT client for this user, or null when Keycloak cannot be asked.</summary>
    public async Task<ChatGptSessionStatus?> GetChatGptSessionAsync(string subject, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Get, $"users/{userId}/sessions", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ChatGptSessionStatus(false, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            DateTimeOffset? lastAccess = null;
            var active = false;
            foreach (var session in document.RootElement.EnumerateArray())
            {
                if (!session.TryGetProperty("clients", out var clients)
                    || clients.ValueKind != JsonValueKind.Object
                    || !clients.EnumerateObject().Any(client =>
                        client.Value.ValueKind == JsonValueKind.String
                        && client.Value.GetString() == _options.ChatGptClientId))
                {
                    continue;
                }

                active = true;
                if (session.TryGetProperty("lastAccess", out var lastAccessValue) && lastAccessValue.TryGetInt64(out var milliseconds))
                {
                    var candidate = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                    lastAccess = lastAccess is null || candidate > lastAccess ? candidate : lastAccess;
                }
            }

            return new ChatGptSessionStatus(active, lastAccess);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken) || exception is JsonException)
        {
            logger.LogWarning("Keycloak session status unavailable: {ExceptionType}.", exception.GetType().Name);
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var client = httpClientFactory.CreateClient(KeycloakOidcClient.HttpClientName);
        var uri = $"{_options.AdminApiBaseUrl}/admin/realms/{Uri.EscapeDataString(_options.RealmName)}/{relativePath}";
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new("Bearer", token);
        var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
        }

        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken);
            var client = httpClientFactory.CreateClient(KeycloakOidcClient.HttpClientName);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.AdminClientId,
                ["client_secret"] = _options.AdminClientSecret,
            });
            using var response = await client.PostAsync(configuration.TokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new KeycloakUnavailableException($"Keycloak admin token request failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var accessToken = document.RootElement.GetProperty("access_token").GetString()
                ?? throw new KeycloakUnavailableException("Keycloak admin token response without access token.");
            var lifetime = document.RootElement.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds)
                ? seconds
                : 60;
            _accessToken = accessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime - 30, 5));
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken() => _accessToken = null;

    private static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is KeycloakUnavailableException or HttpRequestException or InvalidOperationException or KeyNotFoundException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}

public static class ExternalIdentityEvents
{
    public static readonly EventId LinkCreated = new(5101, nameof(LinkCreated));
    public static readonly EventId LinkRejected = new(5102, nameof(LinkRejected));
    public static readonly EventId LinkRevoked = new(5103, nameof(LinkRevoked));
    public static readonly EventId ProviderSessionsEnded = new(5104, nameof(ProviderSessionsEnded));
    public static readonly EventId ProviderSessionsFailed = new(5105, nameof(ProviderSessionsFailed));
    public static readonly EventId BearerRejected = new(5106, nameof(BearerRejected));
    public static readonly EventId BearerRateLimited = new(5107, nameof(BearerRateLimited));
}
