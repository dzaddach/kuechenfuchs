using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kuechenfuchs.Api.ExternalIdentity;

/// <summary>
/// Minimal confidential OIDC client for the explicit linking flow: builds the authorization request and
/// redeems the code at Keycloak. Token handling uses Microsoft.IdentityModel; no token is returned to the browser.
/// </summary>
public sealed class KeycloakOidcClient(
    IHttpClientFactory httpClientFactory,
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    IOptions<KeycloakOptions> options,
    ILogger<KeycloakOidcClient> logger)
{
    public const string HttpClientName = "keycloak";
    private readonly KeycloakOptions _options = options.Value;

    public async Task<string> BuildAuthorizationUrlAsync(
        string state,
        string nonce,
        string codeChallenge,
        CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        return QueryHelpers.AddQueryString(
            configuration.AuthorizationEndpoint,
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = _options.LinkClientId,
                ["redirect_uri"] = _options.LinkRedirectUri,
                ["scope"] = "openid",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                // Always require an explicit login at Keycloak; never link a pre-existing SSO session silently.
                ["prompt"] = "login",
            });
    }

    /// <summary>Redeems the authorization code and returns the verified (issuer, subject) or null.</summary>
    public async Task<VerifiedExternalIdentity?> RedeemCodeAsync(
        string code,
        string codeVerifier,
        string expectedNonceHash,
        CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.LinkRedirectUri,
            ["client_id"] = _options.LinkClientId,
            ["client_secret"] = _options.LinkClientSecret,
            ["code_verifier"] = codeVerifier,
        });
        using var response = await client.PostAsync(configuration.TokenEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Keycloak rejected the linking code exchange with HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        string? idToken;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            idToken = document.RootElement.TryGetProperty("id_token", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            logger.LogWarning("Keycloak returned an unreadable token response.");
            return null;
        }

        if (string.IsNullOrEmpty(idToken))
        {
            logger.LogWarning("Keycloak token response did not contain an ID token.");
            return null;
        }

        var result = await ValidateIdTokenAsync(idToken, configuration);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            configurationManager.RequestRefresh();
            result = await ValidateIdTokenAsync(idToken, await GetConfigurationAsync(cancellationToken));
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
        {
            logger.LogWarning("Linking ID token validation failed: {Reason}", result.Exception?.GetType().Name);
            return null;
        }

        var failure = KeycloakTokenValidation.CheckIdToken(token, _options, expectedNonceHash);
        if (failure is not null)
        {
            logger.LogWarning("Linking ID token rejected: {Reason}", failure);
            return null;
        }

        return new VerifiedExternalIdentity(token.Issuer, token.Subject);
    }

    private Task<TokenValidationResult> ValidateIdTokenAsync(string idToken, OpenIdConnectConfiguration configuration) =>
        new JsonWebTokenHandler().ValidateTokenAsync(
            idToken,
            KeycloakTokenValidation.CreateIdTokenParameters(_options, configuration.SigningKeys));

    private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurationManager.GetConfigurationAsync(cancellationToken);
        if (!string.Equals(configuration.Issuer, _options.Issuer, StringComparison.Ordinal)
            || !configuration.AuthorizationEndpoint.StartsWith(_options.Issuer + "/", StringComparison.Ordinal)
            || string.IsNullOrEmpty(configuration.TokenEndpoint))
        {
            throw new KeycloakUnavailableException("Keycloak discovery metadata does not match the configured issuer.");
        }

        return configuration;
    }
}

public sealed record VerifiedExternalIdentity(string Issuer, string Subject);

public sealed class KeycloakUnavailableException(string message) : Exception(message);
