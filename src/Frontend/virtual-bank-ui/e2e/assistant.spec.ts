import { expect, test } from '@playwright/test';
import { openFromNav, signIn } from './support';

/** Deterministic offline-only tests. Default runtime is real Ollama, not a keyword router. */
test.describe('assistant offline regression', () => {
  test.skip(
    process.env['ASSISTANT_E2E_OFFLINE'] !== '1',
    'Run with explicit ASSISTANT_PROVIDER=Offline for keyword regressions.',
  );
  test('advisor gets her customers and a deterministic notice for the credit part', async ({
    page,
  }) => {
    await signIn(page, 'anna.berater');
    await openFromNav(page, 'Assistant');

    await page.getByTestId('assistant-input').fill('Zeig mir meine Kunden und Kreditanträge');
    await page.getByTestId('assistant-send').click();

    const turn = page.getByTestId('assistant-turn');
    await expect(turn.getByTestId('assistant-result-table')).toContainText('C-1002');
    await expect(turn.getByTestId('assistant-result-table')).not.toContainText('C-5001');
    await expect(turn.getByTestId('assistant-notice')).toContainText('CREDIT_READ_REQUIRED');
    await expect(turn.getByTestId('assistant-notice')).toContainText(
      'does not include access to credit applications',
    );

    await turn.locator('summary').click();
    await expect(turn.getByTestId('assistant-tools')).toContainText('DENIED');
  });

  test('Turkish keyboard variants show the same customer table', async ({ page }) => {
    await signIn(page, 'anna.berater');
    await openFromNav(page, 'Assistant');
    for (const question of [
      'müşterilerimi göster',
      'musterilerimi goster',
      'müsterilerimi göster',
    ]) {
      await page.getByTestId('assistant-input').fill(question);
      await page.getByTestId('assistant-send').click();
      const turn = page.getByTestId('assistant-turn').last();
      await expect(turn.getByTestId('assistant-result-table')).toContainText('C-1002');
      await expect(turn.getByTestId('assistant-result-table')).toContainText('Jana Weidner');
      await expect(turn.getByTestId('assistant-result-table')).not.toContainText('C-5001');
    }
  });

  test('money requests produce authorized balance totals instead of just a customer list', async ({
    page,
  }) => {
    await signIn(page, 'anna.berater');
    await openFromNav(page, 'Assistant');
    await page.getByTestId('assistant-input').fill('show my customer total money');
    await page.getByTestId('assistant-send').click();
    const turn = page.getByTestId('assistant-turn').last();
    await expect(turn.getByTestId('assistant-answer')).toContainText('account balances');
    await expect(turn.getByTestId('assistant-result-table')).toContainText('Account balance');
    await expect(turn.getByTestId('assistant-result-table')).not.toContainText('C-5001');
    await turn.locator('summary').click();
    await expect(turn.getByTestId('assistant-tools')).toContainText('summarize_customer_balances');
  });

  test('platform admin can see its limited capability catalog', async ({ page }) => {
    await signIn(page, 'lena.admin');
    await openFromNav(page, 'Assistant');

    await expect(page.getByTestId('assistant-suggestions')).toContainText('Yetkilerim');
    await expect(page.getByTestId('assistant-suggestions')).not.toContainText('Müşteri bakiyeleri');
  });
});
