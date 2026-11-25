/**
 * A prepared, not yet executed, change (credit decision, note, payment order …). Preparing changed nothing; only
 * the same user confirming the card before it expires executes it, and the API re-checks everything then.
 */
export interface ActionFact {
  readonly key: string;
  readonly value: string;
}

export interface PendingAction {
  readonly id: string;
  readonly kind: string;
  readonly facts: readonly ActionFact[];
  readonly expiresAt: string;
}

export interface ActionOutcome {
  readonly kind: string;
  readonly outcome: string;
  readonly resourceId: string;
  readonly facts: readonly ActionFact[];
}

export const ACTION_KINDS = [
  'credit.approve',
  'credit.reject',
  'credit.note',
  'self.transfer',
  'self.payment.cancel',
  'self.standing-order.pause',
  'self.standing-order.resume',
  'self.internal-transfer',
  'self.standing-order.create',
  'cash.deposit',
  'cash.withdrawal',
  'credit.request',
] as const;

const ACTION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isActionId(value: string): boolean {
  return ACTION_ID.test(value);
}

/** Defensive parsing of a card from the API (also used for cards embedded in assistant replies). */
export function parsePendingAction(raw: unknown): PendingAction | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const r = raw as Record<string, unknown>;
  if (typeof r['id'] !== 'string' || !isActionId(r['id'])) return null;
  if (typeof r['kind'] !== 'string' || !(ACTION_KINDS as readonly string[]).includes(r['kind']))
    return null;
  if (typeof r['expiresAt'] !== 'string' || Number.isNaN(Date.parse(r['expiresAt']))) return null;
  const facts = Array.isArray(r['facts'])
    ? r['facts'].flatMap((f): ActionFact[] => {
        const x = (typeof f === 'object' && f !== null ? f : {}) as Record<string, unknown>;
        return typeof x['key'] === 'string' &&
          /^[a-zA-Z]{1,40}$/.test(x['key']) &&
          typeof x['value'] === 'string'
          ? [{ key: x['key'], value: x['value'].slice(0, 600) }]
          : [];
      })
    : [];
  return { id: r['id'], kind: r['kind'], facts: facts.slice(0, 12), expiresAt: r['expiresAt'] };
}

export function secondsLeft(action: PendingAction, now: number): number {
  return Math.max(0, Math.floor((Date.parse(action.expiresAt) - now) / 1000));
}
