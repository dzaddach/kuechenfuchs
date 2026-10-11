using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kuechenfuchs.Api.ExternalIdentity;

/// <summary>
/// Validation rules for Keycloak-issued tokens. Signature, issuer, audience and lifetime are checked by
/// Microsoft.IdentityModel; this class only adds the Keycloak-specific claim checks on top.
/// </summary>
public static class KeycloakTokenValidation
{
    public const string AccessTokenType = "Bearer";
    public const string IdTokenType = "ID";
    public static readonly string[] AllowedAlgorithms = [SecurityAlgorithms.RsaSha256];

    public static TokenValidationParameters CreateAccessTokenParameters(KeycloakOptions options) =>
        CreateParameters(options, options.ApiAudience);

    public static TokenValidationParameters CreateIdTokenParameters(
        KeycloakOptions options,
        IEnumerable<SecurityKey> signingKeys)
    {
        var parameters = CreateParameters(options, options.LinkClientId);
        parameters.IssuerSigningKeys = signingKeys;
        return parameters;
    }

    /// <summary>Checks a signature-validated access token and returns its external identity and scopes.</summary>
    public static AccessTokenCheck CheckAccessToken(JsonWebToken token, KeycloakOptions options)
    {
        if (!string.Equals(token.Issuer, options.Issuer, StringComparison.Ordinal))
        {
            return AccessTokenCheck.Fail("unexpected issuer");
        }

        if (!HasClaimValue(token, "typ", AccessTokenType))
        {
            return AccessTokenCheck.Fail("not an access token");
        }

        if (!token.TryGetPayloadValue<string>("azp", out var authorizedParty)
            || !options.AllowedClientIds.Contains(authorizedParty, StringComparer.Ordinal))
        {
            return AccessTokenCheck.Fail("client not allowed");
        }

        if (!IsValidSubject(token.Subject))
        {
            return AccessTokenCheck.Fail("missing subject");
        }

        var scopes = token.TryGetPayloadValue<string>("scope", out var scope) && scope.Length <= 2_000
            ? scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        return new AccessTokenCheck(true, null, token.Issuer, token.Subject, authorizedParty, scopes);
    }

    /// <summary>Checks a signature-validated ID token from the linking client, including the one-time nonce.</summary>
    public static string? CheckIdToken(JsonWebToken token, KeycloakOptions options, string expectedNonceHash)
    {
        if (!string.Equals(token.Issuer, options.Issuer, StringComparison.Ordinal))
        {
            return "unexpected issuer";
        }

        if (!HasClaimValue(token, "typ", IdTokenType))
        {
            return "not an ID token";
        }

        if (token.TryGetPayloadValue<string>("azp", out var authorizedParty)
            && !string.Equals(authorizedParty, options.LinkClientId, StringComparison.Ordinal))
        {
            return "unexpected authorized party";
        }

        if (!token.TryGetPayloadValue<string>("nonce", out var nonce)
            || !LinkSecrets.HashMatches(nonce, expectedNonceHash))
        {
            return "nonce mismatch";
        }

        return IsValidSubject(token.Subject) ? null : "missing subject";
    }

    private static TokenValidationParameters CreateParameters(KeycloakOptions options, string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = AllowedAlgorithms,
        ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
        NameClaimType = "sub",
    };

    private static bool HasClaimValue(JsonWebToken token, string claim, string expected) =>
        token.TryGetPayloadValue<string>(claim, out var value) && string.Equals(value, expected, StringComparison.Ordinal);

    private static bool IsValidSubject(string? subject) => !string.IsNullOrWhiteSpace(subject) && subject.Length <= 255;
}

public sealed record AccessTokenCheck(
    bool Succeeded,
    string? Failure,
    string Issuer,
    string Subject,
    string ClientId,
    IReadOnlyList<string> Scopes)
{
    public static AccessTokenCheck Fail(string failure) => new(false, failure, string.Empty, string.Empty, string.Empty, []);
}
