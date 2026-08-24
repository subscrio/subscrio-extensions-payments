using Microsoft.Extensions.Configuration;
using Npgsql;
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;

namespace Subscrio.Payments.Tests.Setup;

public sealed class PaymentsTestContext
{
    public required string DbName { get; init; }
    public required string ConnectionString { get; init; }
    public required Subscrio.Core.Subscrio Subscrio { get; init; }
}

public static class TestDatabase
{
    private static IConfiguration? _configuration;

    private static IConfiguration GetConfiguration()
    {
        if (_configuration != null)
            return _configuration;

        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return _configuration;
    }

    private static string GetAdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrEmpty(env))
            return env;

        var config = GetConfiguration();
        var configConnectionString = config["TestDatabase:ConnectionString"];
        if (!string.IsNullOrEmpty(configConnectionString))
            return configConnectionString;

        return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    }

    public static async Task<PaymentsTestContext> SetupAsync()
    {
        var baseUrl = GetAdminConnectionString();
        var dbName = $"subscrio_payments_test_{Guid.NewGuid():N}";

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseUrl) { Database = "postgres" };
        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {dbName}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(baseUrl) { Database = dbName };
        var connectionString = testBuilder.ConnectionString;

        var subscrio = new Subscrio.Core.Subscrio(new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 5,
                DatabaseType = DatabaseType.PostgreSQL
            }
        });

        await subscrio.InstallSchemaAsync("test-admin-passphrase");

        return new PaymentsTestContext
        {
            DbName = dbName,
            ConnectionString = connectionString,
            Subscrio = subscrio
        };
    }

    public static async Task TeardownAsync(string dbName, Subscrio.Core.Subscrio? subscrio)
    {
        subscrio?.Dispose();

        var adminBuilder = new NpgsqlConnectionStringBuilder(GetAdminConnectionString())
        {
            Database = "postgres"
        };

        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();

        await using (var terminate = new NpgsqlCommand($@"
            SELECT pg_terminate_backend(pg_stat_activity.pid)
            FROM pg_stat_activity
            WHERE pg_stat_activity.datname = '{dbName}'
              AND pid <> pg_backend_pid()", admin))
        {
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName}", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
