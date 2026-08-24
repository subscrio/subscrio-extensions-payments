using Npgsql;
using static Subscrio.Payments.Schema.Migrations;

namespace Subscrio.Payments.Schema;

public sealed class SchemaInstaller
{
    private readonly NpgsqlDataSource _dataSource;

    public SchemaInstaller(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, transaction, "CREATE SCHEMA IF NOT EXISTS subscrio", cancellationToken);

            await ExecuteAsync(connection, transaction, """
                CREATE TABLE IF NOT EXISTS subscrio.payments (
                  id BIGSERIAL PRIMARY KEY,
                  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                  amount_paid INTEGER NOT NULL,
                  currency TEXT NOT NULL,
                  customer_id BIGINT NULL REFERENCES subscrio.customers(id) ON DELETE SET NULL,
                  subscription_id BIGINT NULL REFERENCES subscrio.subscriptions(id) ON DELETE SET NULL,
                  billing_cycle_id BIGINT NULL REFERENCES subscrio.billing_cycles(id) ON DELETE SET NULL,
                  external_product_id TEXT,
                  duration_value INTEGER,
                  duration_unit TEXT,
                  stripe_invoice_id TEXT NOT NULL UNIQUE,
                  stripe_event_id TEXT
                )
                """, cancellationToken);

            await ExecuteAsync(connection, transaction, """
                CREATE INDEX IF NOT EXISTS idx_payments_customer_id
                  ON subscrio.payments (customer_id)
                """, cancellationToken);

            await ExecuteAsync(connection, transaction, """
                CREATE INDEX IF NOT EXISTS idx_payments_subscription_id
                  ON subscrio.payments (subscription_id)
                """, cancellationToken);

            await ExecuteAsync(connection, transaction, """
                CREATE INDEX IF NOT EXISTS idx_payments_created_at
                  ON subscrio.payments (created_at DESC)
                """, cancellationToken);

            await UpsertVersionAsync(connection, transaction, PaymentsSchemaVersion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<string?> VerifyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT config_value FROM subscrio.system_config WHERE config_key = @key LIMIT 1",
                connection);
            cmd.Parameters.AddWithValue("key", PaymentsSchemaVersionKey);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result as string;
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var current = await VerifyAsync(cancellationToken);
        if (current == null)
        {
            await InstallAsync(cancellationToken);
            return 1;
        }

        var applied = 0;
        foreach (var migration in PaymentsMigrations)
        {
            if (CompareVersions(current, migration.Version) >= 0)
                continue;

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await migration.Up(connection, transaction);
                await UpsertVersionAsync(connection, transaction, migration.Version, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                current = migration.Version;
                applied++;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return applied;
    }

    private static async Task UpsertVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string version,
        CancellationToken cancellationToken)
    {
        await using (var select = new NpgsqlCommand(
            "SELECT id FROM subscrio.system_config WHERE config_key = @key LIMIT 1",
            connection, transaction))
        {
            select.Parameters.AddWithValue("key", PaymentsSchemaVersionKey);
            var existing = await select.ExecuteScalarAsync(cancellationToken);

            if (existing != null)
            {
                await using var update = new NpgsqlCommand("""
                    UPDATE subscrio.system_config
                    SET config_value = @value, updated_at = NOW()
                    WHERE config_key = @key
                    """, connection, transaction);
                update.Parameters.AddWithValue("value", version);
                update.Parameters.AddWithValue("key", PaymentsSchemaVersionKey);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO subscrio.system_config (config_key, config_value, encrypted, created_at, updated_at)
                    VALUES (@key, @value, FALSE, NOW(), NOW())
                    """, connection, transaction);
                insert.Parameters.AddWithValue("key", PaymentsSchemaVersionKey);
                insert.Parameters.AddWithValue("value", version);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, transaction);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
