import { defineConfig, devices } from '@playwright/test';

const apiPort = Number(process.env.E2E_API_PORT ?? 5181);

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://localhost:4173',
    locale: 'vi-VN',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'node tests/e2e/start-backend.mjs',
      url: `http://localhost:${apiPort}/api/health`,
      timeout: 240_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'npx vite build && npx vite preview',
      url: 'http://localhost:4173',
      timeout: 240_000,
      reuseExistingServer: false,
      // No AdSense in E2E builds (external ad script, console noise).
      env: { API_PROXY_TARGET: `http://localhost:${apiPort}`, PUBLIC_ADSENSE_CLIENT: '' },
    },
  ],
});
