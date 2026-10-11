using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Kuechenfuchs.Api.Data;

public sealed class KuechenfuchsDbContextFactory : IDesignTimeDbContextFactory<KuechenfuchsDbContext>
{
    public KuechenfuchsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var settings = configuration.GetSection("Database");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = settings["Host"] ?? "localhost",
            Port = settings.GetValue("Port", 5432),
            Database = settings["Name"] ?? "kuechenfuchs",
            Username = settings["User"] ?? "kuechenfuchs",
            Password = settings["Password"] ?? string.Empty,
            GssEncryptionMode = settings.GetValue("GssEncryptionMode", GssEncryptionMode.Disable),
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<KuechenfuchsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new KuechenfuchsDbContext(options);
    }
}
