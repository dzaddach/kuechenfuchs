using System.Security.Claims;
using System.Security.Cryptography;
using Kuechenfuchs.Api.ExternalIdentity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class KeycloakTokenValidationTests : IDisposable
{
    private const string Issuer = "https://kf.example.test/realms/kuechenfuchs";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _foreignRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly KeycloakOptions _options = new()
    {
        Enabled = true,
        Issuer = Issuer,
        MetadataAddress = Issuer + "/.well-known/openid-configuration",
        LinkRedirectUri = "https://kf.example.test/api/v1/integrations/keycloak/callback",
        LinkClientSecret = "unit-test-only",
        AllowedClientIds = ["chatgpt-actions"],
        ClockSkewSeconds = 0,
    };

    public KeycloakTokenValidationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "kf-test" };
    }

    [Fact]
    public async Task AcceptsValidAccessTokenAndReadsScopes()
    {
        var token = await ValidateAccessTokenAsync(CreateAccessToken());

        var check = KeycloakTokenValidation.CheckAccessToken(token!, _options);

        Assert.True(check.Succeeded);
        Assert.Equal(Issuer, check.Issuer);
        Assert.Equal("keycloak-user-1", check.Subject);
        Assert.Equal(["openid", "recipes:read"], check.Scopes);
    }

    [Fact]
    public async Task RejectsWrongIssuer() =>
        Assert.Null(await ValidateAccessTokenAsync(CreateAccessToken(issuer: "https://evil.example.test/realms/kuechenfuchs")));

    [Fact]
    public async Task RejectsWrongAudience() =>
        Assert.Null(await ValidateAccessTokenAsync(CreateAccessToken(audience: "account")));

    [Fact]
    public async Task RejectsExpiredToken() =>
        Assert.Null(await ValidateAccessTokenAsync(CreateAccessToken(expires: DateTime.UtcNow.AddSeconds(-5))));

    [Fact]
    public async Task RejectsTokenSignedWithForeignKey() =>
        Assert.Null(await ValidateAccessTokenAsync(CreateAccessToken(signingKey: new RsaSecurityKey(_foreignRsa) { KeyId = "kf-test" })));

    [Fact]
    public async Task RejectsTamperedPayload()
    {
        var parts = CreateAccessToken().Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("keycloak-user-1", "keycloak-user-2", StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.Null(await ValidateAccessTokenAsync(tampered));
    }

    [Fact]
    public async Task RejectsUnsignedToken()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = _options.ApiAudience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = AccessClaims(),
        });

        Assert.Null(await ValidateAccessTokenAsync(unsigned));
    }

    [Fact]
    public async Task RejectsSymmetricAlgorithm()
    {
        var symmetric = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "kf-test" };
        var token = CreateAccessToken(credentials: new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256));

        Assert.Null(await ValidateAccessTokenAsync(token, symmetric));
    }

    [Fact]
    public async Task RejectsIdTokenOnBearerEndpoint()
    {
        var claims = AccessClaims();
        claims["typ"] = "ID";
        var token = await ValidateAccessTokenAsync(CreateAccessToken(claims: claims));

        Assert.False(KeycloakTokenValidation.CheckAccessToken(token!, _options).Succeeded);
    }

    [Fact]
    public async Task RejectsTokenFromClientThatIsNotAllowed()
    {
        var claims = AccessClaims();
        claims["azp"] = "kuechenfuchs-link";
        var token = await ValidateAccessTokenAsync(CreateAccessToken(claims: claims));

        Assert.False(KeycloakTokenValidation.CheckAccessToken(token!, _options).Succeeded);
    }

    [Fact]
    public async Task RejectsTokenWithoutSubject()
    {
        var claims = AccessClaims();
        claims.Remove("sub");
        var token = await ValidateAccessTokenAsync(CreateAccessToken(claims: claims));

        Assert.False(KeycloakTokenValidation.CheckAccessToken(token!, _options).Succeeded);
    }

    [Fact]
    public async Task AcceptsIdTokenOnlyWithMatchingNonce()
    {
        const string nonce = "expected-nonce-value";
        var idToken = CreateIdToken(nonce);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            idToken,
            KeycloakTokenValidation.CreateIdTokenParameters(_options, [_key]));

        Assert.True(result.IsValid);
        var token = (JsonWebToken)result.SecurityToken;
        Assert.Null(KeycloakTokenValidation.CheckIdToken(token, _options, LinkSecrets.Hash(nonce)));
        Assert.Equal("nonce mismatch", KeycloakTokenValidation.CheckIdToken(token, _options, LinkSecrets.Hash("other")));
    }

    [Fact]
    public async Task RejectsAccessTokenAsLinkingIdToken()
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            CreateAccessToken(),
            KeycloakTokenValidation.CreateIdTokenParameters(_options, [_key]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ComputesPkceChallengeFromRfc7636Example() =>
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            LinkSecrets.CreateS256CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void HashComparisonRejectsMissingAndDifferentValues()
    {
        var value = LinkSecrets.CreateRandomValue();
        var hash = LinkSecrets.Hash(value);

        Assert.True(LinkSecrets.HashMatches(value, hash));
        Assert.False(LinkSecrets.HashMatches(null, hash));
        Assert.False(LinkSecrets.HashMatches(LinkSecrets.CreateRandomValue(), hash));
        Assert.NotEqual(value, LinkSecrets.CreateRandomValue());
    }

    [Fact]
    public void OptionsRejectLinkClientForBearerAccess()
    {
        _options.AllowedClientIds = ["chatgpt-actions", _options.LinkClientId];

        Assert.Throws<InvalidOperationException>(() => _options.Validate(isDevelopment: true));
    }

    [Fact]
    public void OptionsRejectPlainHttpOutsideDevelopment()
    {
        _options.RequireHttpsMetadata = false;
        Assert.Throws<InvalidOperationException>(() => _options.Validate(isDevelopment: false));

        _options.RequireHttpsMetadata = true;
        _options.Issuer = "http://kf.example.test/realms/kuechenfuchs";
        Assert.Throws<InvalidOperationException>(() => _options.Validate(isDevelopment: true));
    }

    [Fact]
    public void OptionsAcceptHttpsConfiguration() => _options.Validate(isDevelopment: false);

    public void Dispose()
    {
        _rsa.Dispose();
        _foreignRsa.Dispose();
    }

    private async Task<JsonWebToken?> ValidateAccessTokenAsync(string token, SecurityKey? key = null)
    {
        var parameters = KeycloakTokenValidation.CreateAccessTokenParameters(_options);
        parameters.IssuerSigningKey = key ?? _key;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        return result.IsValid ? (JsonWebToken)result.SecurityToken : null;
    }

    private Dictionary<string, object> AccessClaims() => new()
    {
        ["sub"] = "keycloak-user-1",
        ["typ"] = "Bearer",
        ["azp"] = "chatgpt-actions",
        ["scope"] = "openid recipes:read",
    };

    private string CreateAccessToken(
        string issuer = Issuer,
        string? audience = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null,
        SigningCredentials? credentials = null,
        Dictionary<string, object>? claims = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience ?? _options.ApiAudience,
            IssuedAt = expiresAt.AddMinutes(-10),
            NotBefore = expiresAt.AddMinutes(-10),
            Expires = expiresAt,
            Claims = claims ?? AccessClaims(),
            SigningCredentials = credentials
                ?? new SigningCredentials(signingKey ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }

    private string CreateIdToken(string nonce) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = _options.LinkClientId,
        Expires = DateTime.UtcNow.AddMinutes(5),
        Subject = new ClaimsIdentity([new Claim("sub", "keycloak-user-1")]),
        Claims = new Dictionary<string, object>
        {
            ["typ"] = "ID",
            ["azp"] = _options.LinkClientId,
            ["nonce"] = nonce,
        },
        SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
    });
}
