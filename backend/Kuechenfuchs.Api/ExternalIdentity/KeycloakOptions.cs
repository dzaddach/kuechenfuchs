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

    public void Validate(bool isDevelopment)
    {
        RequireAbsoluteUri(Issuer, nameof(Issuer));
        RequireAbsoluteUri(MetadataAddress, nameof(MetadataAddress));
        RequireAbsoluteUri(LinkRedirectUri, nameof(LinkRedirectUri));
        if (Issuer.EndsWith('/'))
        {
            throw new InvalidOperationException("Keycloak:Issuer must not end with '/'.");
        }

        if (!RequireHttpsMetadata && !isDevelopment)
        {
            throw new InvalidOperationException("Keycloak:RequireHttpsMetadata may only be disabled in Development.");
        }

        if (RequireHttpsMetadata
            && (!Issuer.StartsWith("https://", StringComparison.Ordinal)
                || !MetadataAddress.StartsWith("https://", StringComparison.Ordinal)
                || !LinkRedirectUri.StartsWith("https://", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Keycloak URLs must use HTTPS unless RequireHttpsMetadata is disabled in Development.");
        }

        if (string.IsNullOrWhiteSpace(ApiAudience)
            || string.IsNullOrWhiteSpace(LinkClientId)
            || string.IsNullOrWhiteSpace(LinkClientSecret))
        {
            throw new InvalidOperationException("Keycloak:ApiAudience, Keycloak:LinkClientId and Keycloak:LinkClientSecret are required.");
        }

        if (AllowedClientIds.Length == 0 || AllowedClientIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Keycloak:AllowedClientIds must contain at least one client ID.");
        }

        if (AllowedClientIds.Contains(LinkClientId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The linking client must not be accepted for bearer API access.");
        }

        if (LinkRequestLifetimeSeconds is < 60 or > 1_800
            || ClockSkewSeconds is < 0 or > 120
            || BackchannelTimeoutSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("Keycloak lifetime, clock skew or timeout settings are out of range.");
        }
    }

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
