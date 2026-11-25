/** UI languages. The API answers assistant questions in the question's language independently of this. */
export type Language = 'en' | 'de' | 'tr';

export const LANGUAGES: readonly { readonly code: Language; readonly name: string }[] = [
  { code: 'en', name: 'English' },
  { code: 'de', name: 'Deutsch' },
  { code: 'tr', name: 'Türkçe' },
];

export function isLanguage(value: unknown): value is Language {
  return value === 'en' || value === 'de' || value === 'tr';
}

/**
 * Explicit user choice first, then the first supported browser language ("de-AT" counts as German),
 * otherwise English.
 */
export function pickLanguage(stored: string | null, browser: readonly string[]): Language {
  if (isLanguage(stored)) return stored;
  for (const tag of browser) {
    const base = tag.toLowerCase().split('-')[0];
    if (isLanguage(base)) return base;
  }
  return 'en';
}
