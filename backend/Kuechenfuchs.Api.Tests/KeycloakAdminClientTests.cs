using System.Text.Json;
using Kuechenfuchs.Api.ExternalIdentity;
using Xunit;

namespace Kuechenfuchs.Api.Tests;

public sealed class KeycloakAdminClientTests
{
    // Real response shape of GET /admin/realms/{realm}/users/{id}/sessions from Keycloak 26.7.5
    // (captured 11.10.2026 at Unix second 1791734488): start/lastAccess are milliseconds.
    private const string Sessions = """
        [
          { "id": "s1", "username": "probe", "userId": "u1", "ipAddress": "172.18.0.1",
            "start": 1791734487000, "lastAccess": 1791734487000, "rememberMe": false,
            "clients": { "01178c8b-446f-4bf9-ab26-45b0d57f82f6": "probe" } },
          { "id": "s2", "start": 1791734400000, "lastAccess": 1791734460000,
            "clients": { "0d7c5f9e-0000-4000-8000-000000000001": "chatgpt-actions" } },
          { "id": "s3", "start": 1791734300000, "lastAccess": 1791734470000,
            "clients": { "0d7c5f9e-0000-4000-8000-000000000001": "chatgpt-actions" } }
        ]
        """;

    [Fact]
    public void InterpretsLastAccessAsUnixMillisecondsAndPicksLatestChatGptSession()
    {
        using var document = JsonDocument.Parse(Sessions);

        var status = KeycloakAdminClient.ParseChatGptSessions(document.RootElement, "chatgpt-actions");

        Assert.True(status.Active);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_791_734_470), status.LastAccessAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero).Date, status.LastAccessAt!.Value.UtcDateTime.Date);
    }

    [Fact]
    public void ReportsNoChatGptSessionWhenOnlyOtherClientsHaveSessions()
    {
        using var document = JsonDocument.Parse(Sessions);

        var status = KeycloakAdminClient.ParseChatGptSessions(document.RootElement, "other-client");

        Assert.False(status.Active);
        Assert.Null(status.LastAccessAt);
    }
}
