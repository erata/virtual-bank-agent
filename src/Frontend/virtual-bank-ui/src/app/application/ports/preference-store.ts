/** Non-sensitive UI preferences kept on this device (never tokens or personal data). */
export abstract class PreferenceStore {
  abstract language(): string | null;

  abstract setLanguage(language: string): void;
}
