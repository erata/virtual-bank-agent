export type CreditApplicationStatus = 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'REJECTED';

export interface CreditApplication {
  readonly id: string;
  readonly customerId: string;
  readonly amount: number;
  readonly currency: string;
  readonly status: CreditApplicationStatus;
  readonly purpose: string;
  readonly version: number;
}

/** A note on an application (including decision notes such as "[REJECTED] reason"). */
export interface CreditNoteEntry {
  readonly id: number;
  readonly byYou: boolean;
  readonly body: string;
  readonly createdAt: string;
}

export interface CreditNote {
  readonly id: number;
  readonly applicationId: string;
  readonly createdAt: string;
}

/** Same rule as the backend value object CreditNoteText (trimmed, 1..4000 characters). */
export const CREDIT_NOTE_MAX_LENGTH = 4000;

/** Same as the API: rejection reason required, approval comment optional, both at most 500 characters. */
export const DECISION_NOTE_MAX_LENGTH = 500;

const APPLICATION_ID = /^K-[0-9]{3}$/;

export function isCreditApplicationId(value: string): boolean {
  return APPLICATION_ID.test(value);
}

export function normalizeNoteText(raw: string): string | null {
  const text = raw.trim();
  return text.length === 0 || text.length > CREDIT_NOTE_MAX_LENGTH ? null : text;
}
