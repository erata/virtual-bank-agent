import { defineConfig } from '@playwright/test';

/**
 * End-to-end tests through the real login: browser → Angular → Keycloak (Authorization Code + PKCE) →
 * same-origin /api proxy → APIs. Point BASE_URL at the running stack (default: `make up`).
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI']
    ? [['list'], ['junit', { outputFile: 'test-results/e2e-junit.xml' }]]
    : 'list',
  use: {
    baseURL: process.env['BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: process.env['CHROME_PATH'] ? { executablePath: process.env['CHROME_PATH'] } : {},
  },
});
