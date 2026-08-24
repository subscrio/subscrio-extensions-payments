import { randomUUID } from 'crypto';
import { Client } from 'pg';
import { Subscrio } from 'subscrio';

export interface TestContext {
  dbName: string;
  connectionString: string;
  subscrio: Subscrio;
}

function baseUrl(): string {
  return (
    process.env.TEST_DATABASE_URL ||
    'postgresql://postgres:postgres@localhost:5432/postgres'
  );
}

export async function setupTestDatabase(): Promise<TestContext> {
  const dbName = `subscrio_payments_test_${randomUUID().replace(/-/g, '')}`;
  const admin = new Client({ connectionString: baseUrl() });

  try {
    await admin.connect();
    await admin.query(`CREATE DATABASE ${dbName}`);
  } finally {
    await admin.end();
  }

  const connectionString = baseUrl().replace(/\/[^/]*$/, `/${dbName}`);
  const subscrio = new Subscrio({
    database: { connectionString },
  });
  await subscrio.installSchema('test-admin-passphrase');

  return { dbName, connectionString, subscrio };
}

export async function teardownTestDatabase(
  dbName: string,
  subscrio?: Subscrio
): Promise<void> {
  if (subscrio) {
    try {
      await subscrio.close();
    } catch {
      // ignore
    }
  }

  if (process.env.KEEP_TEST_DB === 'true') {
    console.log(`KEEP_TEST_DB: preserving ${dbName}`);
    return;
  }

  const admin = new Client({ connectionString: baseUrl() });
  try {
    await admin.connect();
    await admin.query(`
      SELECT pg_terminate_backend(pg_stat_activity.pid)
      FROM pg_stat_activity
      WHERE pg_stat_activity.datname = '${dbName}'
        AND pid <> pg_backend_pid()
    `);
    await admin.query(`DROP DATABASE IF EXISTS ${dbName}`);
  } finally {
    await admin.end();
  }
}
