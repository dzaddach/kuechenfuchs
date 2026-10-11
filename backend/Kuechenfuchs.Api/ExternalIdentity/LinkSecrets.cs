using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kuechenfuchs.Api.ExternalIdentity;

/// <summary>Random values and hashes for the one-time linking flow. No custom OAuth cryptography beyond PKCE S256 (RFC 7636).</summary>
public static class LinkSecrets
{
    public static string CreateRandomValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool HashMatches(string? value, string expectedHash)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(value)),
            Encoding.ASCII.GetBytes(expectedHash));
    }

    public static string CreateS256CodeChallenge(string codeVerifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
}
