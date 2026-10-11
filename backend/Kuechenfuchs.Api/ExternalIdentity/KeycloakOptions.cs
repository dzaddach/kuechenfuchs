namespace Kuechenfuchs.Api.ExternalIdentity;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public bool Enabled { get; set; }

    /// <summary>Exact public issuer, e.g. https://example.test/realms/kuechenfuchs. Never derived from requests.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Discovery document address used by the API (may be an internal Compose URL).</summary>
    public string MetadataAddress { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;
    public string ApiAudience { get; set; } = "kuechenfuchs-api";

    /// <summary>OAuth clients (azp) whose access tokens are accepted by the ChatGPT bearer endpoints.</summary>
    public string[] AllowedClientIds { get; set; } = [];

    public string LinkClientId { get; set; } = "kuechenfuchs-link";
    public string LinkClientSecret { get; set; } = string.Empty;

    /// <summary>Exact redirect URI registered for the linking client. Never derived from requests.</summary>
    public string LinkRedirectUri { get; set; } = string.Empty;

    public int LinkRequestLifetimeSeconds { get; set; } = 600;
    public int ClockSkewSeconds { get; set; } = 30;
    public int BackchannelTimeoutSeconds { get; set; } = 10;

    /// <summary>Internal Keycloak base URL incl. context path for the Admin REST API, e.g. http://keycloak:8080/auth.</summary>
    public string AdminApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Service account used only to end Keycloak sessions and read session status.</summary>
    public string AdminClientId { get; set; } = "kuechenfuchs-api-admin";
    public string AdminClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth client used by ChatGPT Actions; must be one of <see cref="AllowedClientIds"/>.</summary>
    public string ChatGptClientId { get; set; } = "chatgpt-actions";

    /// <summary>Per linked identity; ChatGPT honours 429 responses.</summary>
    public int BearerRequestsPerMinute { get; set; } = 60;

    /// <summary>Realm name taken from the fixed issuer (…/realms/{name}).</summary>
    public string RealmName
    {
        get
        {
            var marker = Issuer.LastIndexOf("/realms/", StringComparison.Ordinal);
            return marker < 0 ? string.Empty : Issuer[(marker + "/realms/".Length)..];
        }
    }

    public void Validate(bool isDevelopment)
    {
        RequireAbsoluteUri(Issuer, nameof(Issuer));
        RequireAbsoluteUri(MetadataAddress, nameof(MetadataAddress));
        RequireAbsoluteUri(LinkRedirectUri, nameof(LinkRedirectUri));
        RequireAbsoluteUri(AdminApiBaseUrl, nameof(AdminApiBaseUrl));
        if (Issuer.EndsWith('/') || AdminApiBaseUrl.EndsWith('/'))
        {
            throw new InvalidOperationException("Keycloak:Issuer and Keycloak:AdminApiBaseUrl must not end with '/'.");
        }

        if (string.IsNullOrEmpty(RealmName) || RealmName.Contains('/'))
        {
            throw new InvalidOperationException("Keycloak:Issuer must end with /realms/{realm}.");
        }

        // Browser-facing URLs are public: HTTPS outside Development.
        if (!isDevelopment && (!IsHttps(Issuer) || !IsHttps(LinkRedirectUri)))
        {
            throw new InvalidOperationException("Keycloak:Issuer and Keycloak:LinkRedirectUri must use HTTPS outside Development.");
        }

        // Backchannel URLs: HTTPS, or plain HTTP only when explicitly allowed and either in Development or
        // addressed to an internal Compose service name (no dot, never routed through the internet).
        foreach (var (value, name) in new[] { (MetadataAddress, nameof(MetadataAddress)), (AdminApiBaseUrl, nameof(AdminApiBaseUrl)) })
        {
            if (!IsHttps(value) && (RequireHttpsMetadata || (!isDevelopment && !IsInternalServiceUri(value))))
            {
                throw new InvalidOperationException(
                    $"Keycloak:{name} must use HTTPS unless RequireHttpsMetadata is false and it targets an internal service name (or Development).");
            }
        }

        if (string.IsNullOrWhiteSpace(ApiAudience)
            || string.IsNullOrWhiteSpace(LinkClientId)
            || string.IsNullOrWhiteSpace(LinkClientSecret)
            || string.IsNullOrWhiteSpace(AdminClientId)
            || string.IsNullOrWhiteSpace(AdminClientSecret))
        {
            throw new InvalidOperationException("Keycloak audience, linking client and admin client settings are required.");
        }

        if (AllowedClientIds.Length == 0 || AllowedClientIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Keycloak:AllowedClientIds must contain at least one client ID.");
        }

        if (AllowedClientIds.Contains(LinkClientId, StringComparer.Ordinal)
            || AllowedClientIds.Contains(AdminClientId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The linking and admin clients must not be accepted for bearer API access.");
        }

        if (!AllowedClientIds.Contains(ChatGptClientId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Keycloak:ChatGptClientId must be one of Keycloak:AllowedClientIds.");
        }

        if (LinkRequestLifetimeSeconds is < 60 or > 1_800
            || ClockSkewSeconds is < 0 or > 120
            || BackchannelTimeoutSeconds is < 1 or > 60
            || BearerRequestsPerMinute is < 1 or > 1_000)
        {
            throw new InvalidOperationException("Keycloak lifetime, clock skew, timeout or rate limit settings are out of range.");
        }
    }

    private static bool IsHttps(string value) => value.StartsWith("https://", StringComparison.Ordinal);

    private static bool IsInternalServiceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.HostNameType == UriHostNameType.Dns && !uri.Host.Contains('.');

    private static void RequireAbsoluteUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"Keycloak:{name} must be an absolute HTTP(S) URL without fragment.");
        }
    }
}
