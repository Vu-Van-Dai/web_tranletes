import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: '.', testMatch: 'ad-frame.spec.ts', use: { browserName: 'chromium' }, workers: 1 });
