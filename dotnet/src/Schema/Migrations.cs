namespace Subscrio.Payments.Schema;

public static class Migrations
{
    public const string PaymentsSchemaVersion = "1.0.0";
    public const string PaymentsSchemaVersionKey = "payments_schema_version";

    /// <summary>
    /// Extension-only migrations keyed by target version.
    /// Currently empty — InstallSchemaAsync creates the full 1.0.0 schema.
    /// </summary>
    public static IReadOnlyList<PaymentsMigration> PaymentsMigrations { get; } =
        Array.Empty<PaymentsMigration>();

    public static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var len = Math.Max(parts1.Length, parts2.Length);

        for (var i = 0; i < len; i++)
        {
            var part1 = i < parts1.Length ? parts1[i] : 0;
            var part2 = i < parts2.Length ? parts2[i] : 0;
            if (part1 < part2) return -1;
            if (part1 > part2) return 1;
        }

        return 0;
    }
}

public sealed class PaymentsMigration
{
    public required string Version { get; init; }
    public required Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task> Up { get; init; }
}
