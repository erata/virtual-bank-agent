import { Injectable, effect, inject, signal } from '@angular/core';
import { PreferenceStore } from '../../application/ports/preference-store';
import { Language, pickLanguage } from '../../domain/language';
import { MESSAGES, MessageKey } from './messages';

const INDEX: Readonly<Record<Language, 0 | 1 | 2>> = { en: 0, de: 1, tr: 2 };

/**
 * Runtime UI translation: switching language re-renders immediately (no reload, no per-locale build).
 * Templates call {@link t}; because it reads the {@link language} signal, OnPush views update on change.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly preferences = inject(PreferenceStore);

  readonly language = signal<Language>(
    pickLanguage(
      this.preferences.language(),
      typeof navigator === 'undefined' ? [] : (navigator.languages ?? [navigator.language]),
    ),
  );

  constructor() {
    effect(() => {
      if (typeof document !== 'undefined') document.documentElement.lang = this.language();
    });
  }

  use(language: Language): void {
    this.language.set(language);
    this.preferences.setLanguage(language);
  }

  /** Translated text; `{name}` placeholders are replaced by params (values are plain text, never HTML). */
  t(key: MessageKey, params?: Readonly<Record<string, string | number>>): string {
    const entry = (MESSAGES as Readonly<Record<string, readonly string[]>>)[key];
    if (!entry) return key;
    const text = entry[INDEX[this.language()]];
    return params
      ? text.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? `{${name}}`))
      : text;
  }
}
