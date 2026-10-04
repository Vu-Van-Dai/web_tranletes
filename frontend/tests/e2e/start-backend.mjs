// Starts the real API for E2E runs against an isolated, freshly created database.
// Only the database named "comicplatform_e2e" is ever dropped/created.
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import pg from 'pg';

const root = path.resolve(import.meta.dirname, '..', '..', '..');
const E2E_DB = 'comicplatform_e2e';
export const E2E_PORT = Number(process.env.E2E_API_PORT ?? 5181);

function readEnv() {
  const file = path.join(root, '.env');
  const values = {};
  if (existsSync(file)) {
    for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
      const m = /^([A-Z0-9_]+)=(.*)$/.exec(line.trim());
      if (m) values[m[1]] = m[2];
    }
  }
  return { ...values, ...process.env };
}

/** Parses an Npgsql connection string ("Host=..;Username=..;Password=..;Database=.."). */
function parseNpgsql(cs) {
  const parts = Object.fromEntries(
    cs.split(';').filter(Boolean).map((kv) => {
      const i = kv.indexOf('=');
      return [kv.slice(0, i).trim().toLowerCase(), kv.slice(i + 1).trim()];
    }),
  );
  return { host: parts.host ?? 'localhost', port: Number(parts.port ?? 5432), user: parts.username ?? parts['user id'] ?? 'postgres', password: parts.password };
}

const env = readEnv();
const conn = parseNpgsql(env.DATABASE_CONNECTION_STRING ?? 'Host=localhost;Username=postgres');

const admin = new pg.Client({ ...conn, database: 'postgres' });
await admin.connect();
await admin.query(`DROP DATABASE IF EXISTS ${E2E_DB} WITH (FORCE)`);
await admin.query(`CREATE DATABASE ${E2E_DB} ENCODING 'UTF8' TEMPLATE template0`);
await admin.end();

const dataDir = path.join(root, 'data', 'e2e');
rmSync(dataDir, { recursive: true, force: true });

const child = spawn('dotnet', ['run', '--project', path.join(root, 'backend', 'src', 'Api'), '-c', 'Release', '--no-launch-profile'], {
  stdio: 'inherit',
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'E2E',
    ASPNETCORE_URLS: `http://localhost:${E2E_PORT}`,
    DATABASE_CONNECTION_STRING: `Host=${conn.host};Port=${conn.port};Database=${E2E_DB};Username=${conn.user};Password=${conn.password};Maximum Pool Size=30`,
    JWT_SECRET: 'e2e-only-jwt-secret-0123456789-abcdefghijklmnop',
    SEED_ADMIN_EMAIL: 'admin@e2e.test',
    SEED_ADMIN_USERNAME: 'e2e_admin',
    SEED_ADMIN_PASSWORD: 'E2E-Admin-Password-123',
    PACKAGE_STORE: 'local',
    COMIC_STORAGE_PATH: path.join(dataDir, 'comics'),
    TEMP_STORAGE_PATH: path.join(dataDir, 'tmp'),
    PUBLIC_IMAGE_URL: '/cdn',
    ORIGIN_SHARED_SECRET: 'e2e-origin-secret-0123456789',
    CORS_ALLOWED_ORIGINS: 'http://localhost:4173',
    RATE_LIMITS_ENABLED: 'false',
    ARGON2_MEMORY_KB: '4096',
    ARGON2_ITERATIONS: '1',
    MIN_FREE_SPACE: '10MB',
    OUTBOX_INTERVAL: '00:00:02',
  },
});

for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.on('exit', (code) => process.exit(code ?? 0));
