/** A transfer order as typed by the customer. No customer ID: the token decides whose account it is. */
export interface TransferInput {
  readonly beneficiaryName: string;
  readonly iban: string;
  readonly amount: number;
  readonly currency: string;
  readonly executionDate: string | null;
  readonly reference: string;
}

/** A new standing order (frequency weekly or monthly). */
export interface StandingOrderInput {
  readonly beneficiaryName: string;
  readonly iban: string;
  readonly amount: number;
  readonly currency: string;
  readonly frequency: 'weekly' | 'monthly';
  readonly firstDate: string | null;
  readonly reference: string;
}

/** Same rules as the API (MyPaymentOrders): checked before the round trip, re-checked by the API. */
export function transferProblems(t: TransferInput, today: string): readonly string[] {
  const problems: string[] = [];
  if (!/^[\p{L}\p{M}0-9 .,'&()/-]{2,70}$/u.test(t.beneficiaryName.trim()))
    problems.push('beneficiaryName');
  if (!isValidIban(t.iban)) problems.push('iban');
  // At most two decimals; compared with a tolerance because 0.29 * 100 is 28.999… in binary floating point.
  if (
    !(t.amount > 0 && t.amount <= 10000) ||
    Math.abs(Math.round(t.amount * 100) - t.amount * 100) > 1e-6
  )
    problems.push('amount');
  if (t.executionDate !== null && t.executionDate < today) problems.push('executionDate');
  if (t.reference.length > 140) problems.push('reference');
  return problems;
}

/** ISO 13616 mod-97 check, identical to the server's. */
export function isValidIban(raw: string): boolean {
  const iban = raw.replace(/\s+/g, '').toUpperCase();
  if (!/^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$/.test(iban)) return false;
  const digits = (iban.slice(4) + iban.slice(0, 4)).replace(/[A-Z]/g, (c) =>
    String(c.charCodeAt(0) - 55),
  );
  let rest = 0;
  for (const d of digits) rest = (rest * 10 + Number(d)) % 97;
  return rest === 1;
}

/** The signed-in bank customer's own data. There is no customer ID input anywhere: the token decides. */
export interface MyProfile {
  readonly id: string;
  readonly branch: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly segment: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly iban: string | null;
  readonly dateOfBirth: string | null;
  readonly sensitiveFieldsMasked: boolean;
}

export interface CurrencyTotal {
  readonly currency: string;
  readonly balance: number;
  readonly accountCount: number;
}

export interface MyBalanceOverview {
  readonly totalsByCurrency: readonly CurrencyTotal[];
}

export interface ScheduledPayment {
  readonly id: string;
  readonly description: string;
  readonly amount: number;
  readonly currency: string;
  readonly dueDate: string;
  readonly status: string;
}

export interface BookedTransaction {
  readonly id: string;
  readonly description: string;
  readonly amount: number;
  readonly currency: string;
  readonly bookedOn: string;
}

export interface StandingOrder {
  readonly id: string;
  readonly description: string;
  readonly amount: number;
  readonly currency: string;
  readonly frequency: string;
  readonly nextDueDate: string;
  readonly active: boolean;
}
