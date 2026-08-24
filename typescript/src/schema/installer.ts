import type { Pool, PoolClient } from 'pg';
import {
  PAYMENTS_MIGRATIONS,
  PAYMENTS_SCHEMA_VERSION,
  PAYMENTS_SCHEMA_VERSION_KEY,
  compareVersions,
} from './migrations.js';

export class PaymentsSchemaInstaller {
  constructor(private readonly pool: Pool) {}

  async install(): Promise<void> {
    const client = await this.pool.connect();
    try {
      await client.query('BEGIN');
      await client.query(`CREATE SCHEMA IF NOT EXISTS subscrio`);

      await client.query(`
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
      `);

      await client.query(`
        CREATE INDEX IF NOT EXISTS idx_payments_customer_id
          ON subscrio.payments (customer_id)
      `);
      await client.query(`
        CREATE INDEX IF NOT EXISTS idx_payments_subscription_id
          ON subscrio.payments (subscription_id)
      `);
      await client.query(`
        CREATE INDEX IF NOT EXISTS idx_payments_created_at
          ON subscrio.payments (created_at DESC)
      `);

      await this.upsertVersion(client, PAYMENTS_SCHEMA_VERSION);
      await client.query('COMMIT');
    } catch (err) {
      await client.query('ROLLBACK');
      throw err;
    } finally {
      client.release();
    }
  }

  async verify(): Promise<string | null> {
    try {
      const result = await this.pool.query<{ config_value: string }>(
        `SELECT config_value FROM subscrio.system_config WHERE config_key = $1 LIMIT 1`,
        [PAYMENTS_SCHEMA_VERSION_KEY]
      );
      return result.rows[0]?.config_value ?? null;
    } catch {
      return null;
    }
  }

  async migrate(): Promise<number> {
    let current = await this.verify();
    if (!current) {
      await this.install();
      return 1;
    }

    let applied = 0;
    for (const migration of PAYMENTS_MIGRATIONS) {
      if (compareVersions(current, migration.version) < 0) {
        const client = await this.pool.connect();
        try {
          await client.query('BEGIN');
          await migration.up(client);
          await this.upsertVersion(client, migration.version);
          await client.query('COMMIT');
          current = migration.version;
          applied++;
        } catch (err) {
          await client.query('ROLLBACK');
          throw err;
        } finally {
          client.release();
        }
      }
    }
    return applied;
  }

  private async upsertVersion(client: PoolClient, version: string): Promise<void> {
    const existing = await client.query(
      `SELECT id FROM subscrio.system_config WHERE config_key = $1 LIMIT 1`,
      [PAYMENTS_SCHEMA_VERSION_KEY]
    );

    if (existing.rows.length > 0) {
      await client.query(
        `UPDATE subscrio.system_config
         SET config_value = $1, updated_at = NOW()
         WHERE config_key = $2`,
        [version, PAYMENTS_SCHEMA_VERSION_KEY]
      );
    } else {
      await client.query(
        `INSERT INTO subscrio.system_config (config_key, config_value, encrypted, created_at, updated_at)
         VALUES ($1, $2, FALSE, NOW(), NOW())`,
        [PAYMENTS_SCHEMA_VERSION_KEY, version]
      );
    }
  }
}
