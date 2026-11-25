import { expect, test } from '@playwright/test';
import { openFromNav, signIn } from './support';

test.describe('same pages, different people', () => {
  test('advisor sees her branch, masked sensitive data and no credit data', async ({ page }) => {
    await signIn(page, 'anna.berater');

    await openFromNav(page, 'Customers');
    const table = page.getByTestId('customer-table');
    await expect(table).toContainText('C-1002');
    await expect(table).not.toContainText('C-5001'); // Munich branch

    await table.getByRole('link', { name: 'C-1002' }).click();
    await expect(page.getByTestId('masked')).toBeVisible();

    // The menu hides what the role cannot use, but the API is the boundary: a direct URL is still denied.
    const nav = page.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'Credit applications', exact: true })).toHaveCount(
      0,
    );
    await page.goto('/credit');
    await expect(page.getByTestId('notice')).toContainText(
      'does not include access to credit applications',
    );
    await expect(page.getByTestId('notice')).toContainText('CREDIT_READ_REQUIRED');
  });

  test('another branch’s customer is indistinguishable from a missing one', async ({ page }) => {
    await signIn(page, 'anna.berater');
    await page.goto('/customers/C-5001');
    await expect(page.getByTestId('not-found')).toBeVisible();
  });

  test('analyst works only on assigned applications and can add a note', async ({ page }) => {
    await signIn(page, 'mehmet.kredit');

    await openFromNav(page, 'Credit applications');
    const table = page.getByTestId('credit-table');
    await expect(table).toContainText('K-100');
    await expect(table).not.toContainText('K-200'); // unassigned draft

    await table.getByRole('link', { name: 'K-100' }).click();
    await page.getByTestId('note-text').fill('Income verified with the last three payslips.');
    await page.getByTestId('note-submit').click();
    await expect(page.getByTestId('note-saved')).toBeVisible();
  });

  test('empty note is rejected before it reaches the API', async ({ page }) => {
    await signIn(page, 'mehmet.kredit');
    await page.goto('/credit/K-100');
    await page.getByTestId('note-text').fill('   ');
    await page.getByTestId('note-submit').click();
    await expect(page.getByRole('alert')).toContainText('1 to 4000 characters');
  });

  test('approver may read but not write notes', async ({ page }) => {
    await signIn(page, 'max.approver');
    await page.goto('/credit/K-100');
    await page.getByTestId('note-text').fill('Looks fine.');
    await page.getByTestId('note-submit').click();
    await expect(page.getByTestId('notice')).toContainText('CREDIT_NOTE_PERMISSION_REQUIRED');
  });

  test('platform admin gets no business data', async ({ page }) => {
    await signIn(page, 'lena.admin');
    await expect(
      page
        .getByRole('navigation', { name: 'Main' })
        .getByRole('link', { name: 'Customers', exact: true }),
    ).toHaveCount(0);
    await page.goto('/customers');
    await expect(page.getByTestId('notice')).toContainText('Access denied');
  });
});

test.describe('session', () => {
  test('a deep link asks for sign-in and returns to the requested page', async ({ page }) => {
    await signIn(page, 'anna.berater', '/customers');
    await expect(page).toHaveURL(/\/customers$/);
    await expect(page.getByTestId('customer-table')).toBeVisible();
  });

  test('identity card shows token attributes', async ({ page }) => {
    await signIn(page, 'anna.berater');
    await openFromNav(page, 'My access');
    await expect(page.getByTestId('branch')).toHaveText('STR');
  });

  test('the access token is sent to the APIs only', async ({ page }) => {
    const leaked: string[] = [];
    page.on('request', (request) => {
      const auth = request.headers()['authorization'];
      if (auth?.startsWith('Bearer ') && !new URL(request.url()).pathname.startsWith('/api/')) {
        leaked.push(request.url());
      }
    });
    await signIn(page, 'mehmet.kredit');
    await openFromNav(page, 'Credit applications');
    await expect(page.getByTestId('credit-table')).toBeVisible();
    expect(leaked).toEqual([]);
  });

  test('sign out ends the session', async ({ page }) => {
    await signIn(page, 'anna.berater');
    // Keycloak's end-session endpoint is called, then the browser is sent back to the app.
    const endSession = page.waitForRequest('**/protocol/openid-connect/logout**');
    await page.getByTestId('current-user').click(); // sign out lives in the user menu
    await page.getByTestId('logout').click();
    await endSession;
    await page.waitForEvent('framenavigated');
    await expect(page.getByTestId('login')).toBeVisible();
    await page.goto('/customers');
    await expect(page.locator('#username')).toBeVisible(); // back at Keycloak
  });
});

test.describe('browser security', () => {
  test('the app runs under the strict Content-Security-Policy without errors', async ({ page }) => {
    const problems: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') problems.push(message.text());
    });
    page.on('pageerror', (error) => problems.push(error.message));

    await signIn(page, 'mehmet.kredit');
    await page.goto('/credit/K-100');
    await expect(page.getByTestId('note-text')).toBeVisible();
    await page.goto('/customers');
    await expect(page.getByTestId('customer-table')).toBeVisible();

    expect(problems).toEqual([]);
  });
});

test.describe('menus and documents follow permissions', () => {
  test('bank customer gets self-service and public documents, no staff menus', async ({ page }) => {
    await signIn(page, 'deniz.customer');
    const nav = page.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'My banking', exact: true })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Customers', exact: true })).toHaveCount(0);

    await openFromNav(page, 'My banking');
    await expect(page.getByTestId('my-accounts')).toContainText('A-20011');
  });

  test('bank customer opens a public document', async ({ page }) => {
    // The document catalog is filled by ingestion, which needs an embedding model. CI starts no
    // model and therefore has no documents; run locally after indexing with E2E_DOCUMENTS_INDEXED=1.
    test.skip(
      process.env['E2E_DOCUMENTS_INDEXED'] !== '1',
      'Needs indexed sample documents (scripts/ai-index.sh or INDEX_DOCUMENTS_WINDOWS.ps1).',
    );
    await signIn(page, 'deniz.customer');
    await openFromNav(page, 'Documents');
    await page.getByTestId('document-table').getByRole('link', { name: 'DOC-118' }).click();
    await expect(page.getByTestId('document-title')).toContainText('SEPA');
    // A PDF opens in its original form; the extracted text is the second view.
    await expect(page.getByTestId('document-pdf')).toBeVisible();
    await page.getByTestId('view-text').click();
    await expect(page.getByTestId('document-text')).toContainText('IBAN');
  });

  test('a document outside the reader ACL is indistinguishable from a missing one', async ({
    page,
  }) => {
    await signIn(page, 'anna.berater');
    await page.goto('/documents/DOC-126');
    await expect(page.getByTestId('not-found')).toBeVisible();
  });
});
