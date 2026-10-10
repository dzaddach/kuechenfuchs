using Kuechenfuchs.Api.Data;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

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

app.Run();
