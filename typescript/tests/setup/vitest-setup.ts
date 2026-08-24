import { config as loadEnv } from 'dotenv';
import { dirname, resolve } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
loadEnv({ path: resolve(__dirname, '../../.env') });
if (!process.env.TEST_DATABASE_URL && !process.env.DATABASE_URL) {
  loadEnv({ path: resolve(__dirname, '../../../../../core/typescript/.env') });
}

if (!process.env.TEST_DATABASE_URL && process.env.DATABASE_URL) {
  process.env.TEST_DATABASE_URL = process.env.DATABASE_URL;
}

if (!process.env.TEST_DATABASE_URL) {
  process.env.TEST_DATABASE_URL =
    'postgresql://postgres:postgres@localhost:5432/postgres';
}

if (!process.env.LOG_LEVEL) {
  process.env.LOG_LEVEL = 'error';
}
