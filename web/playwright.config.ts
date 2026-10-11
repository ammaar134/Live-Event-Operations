import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './e2e', fullyParallel: false, workers: 1, retries: 0, timeout: 40000, use: { baseURL: process.env.BASE_URL || 'http://127.0.0.1:5175', trace: 'retain-on-failure', screenshot: 'only-on-failure' }, reporter: [['list'], ['html', { open: 'never' }]] });
