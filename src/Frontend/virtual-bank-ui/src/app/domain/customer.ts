export interface CustomerSummary {
  readonly id: string;
  readonly branch: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly segment: string;
}

/** Sensitive fields are null when the caller lacks customer.read-sensitive; the API says so explicitly. */
export interface CustomerDetail extends CustomerSummary {
  readonly email: string | null;
  readonly phone: string | null;
  readonly iban: string | null;
  readonly dateOfBirth: string | null;
  readonly sensitiveFieldsMasked: boolean;
}

export interface Account {
  readonly id: string;
  readonly type: string;
  readonly currency: string;
  readonly balance: number;
}

const CUSTOMER_ID = /^C-[0-9]{4}$/;

export function isCustomerId(value: string): boolean {
  return CUSTOMER_ID.test(value);
}
