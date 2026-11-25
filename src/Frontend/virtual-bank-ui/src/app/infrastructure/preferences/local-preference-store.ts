import { Injectable } from '@angular/core';
import { PreferenceStore } from '../../application/ports/preference-store';

const LANGUAGE_KEY = 'vb.ui-language';

/**
 * Adapter: the UI language in localStorage. This is the only browser storage outside the auth adapter and it
 * holds a single two-letter code; storage being unavailable (private mode, policy) simply means "no choice yet".
 */
@Injectable()
export class LocalPreferenceStore extends PreferenceStore {
  language(): string | null {
    try {
      return localStorage.getItem(LANGUAGE_KEY);
    } catch {
      return null;
    }
  }

  setLanguage(language: string): void {
    try {
      localStorage.setItem(LANGUAGE_KEY, language);
    } catch {
      // Not persisted; the choice still applies for this session.
    }
  }
}
