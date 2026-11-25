import { Page, expect } from '@playwright/test';

/** Development-only persona password from the local demo realm (see README, security notice). */
export const DEV_PASSWORD = 'VirtualBank-Dev-2026!';

/** Signs in on the real Keycloak login page and waits until the app shows the user. */
export async function signIn(page: Page, username: string, path = '/'): Promise<void> {
  await page.goto(path);
  if (path === '/') {
    await page.getByTestId('login').click();
  }
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(DEV_PASSWORD);
  await page.locator('#kc-login').click();
  await expect(page.getByTestId('current-user')).toBeVisible();
}

/** Clicks an entry of the main navigation (the home page has similarly named buttons). */
export async function openFromNav(page: Page, name: string): Promise<void> {
  await page
    .getByRole('navigation', { name: 'Main' })
    .getByRole('link', { name, exact: true })
    .click();
}
