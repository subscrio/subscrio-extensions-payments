export const PAYMENTS_SCHEMA_VERSION = '1.0.0';
export const PAYMENTS_SCHEMA_VERSION_KEY = 'payments_schema_version';

/**
 * Extension-only migrations keyed by target version.
 * Currently empty — installSchema creates the full 1.0.0 schema.
 */
export type PaymentsMigration = {
  version: string;
  up: (client: { query: (sql: string, params?: unknown[]) => Promise<unknown> }) => Promise<void>;
};

export const PAYMENTS_MIGRATIONS: PaymentsMigration[] = [];

export function compareVersions(v1: string, v2: string): number {
  const parts1 = v1.split('.').map(Number);
  const parts2 = v2.split('.').map(Number);

  for (let i = 0; i < Math.max(parts1.length, parts2.length); i++) {
    const part1 = parts1[i] || 0;
    const part2 = parts2[i] || 0;
    if (part1 < part2) return -1;
    if (part1 > part2) return 1;
  }
  return 0;
}
