/**
 * Outcome of a call to a backend use case, independent of HTTP. Mirrors the backend's UseCaseResult:
 * "not-found" deliberately covers both "does not exist" and "not visible to you".
 */
export type Result<T> =
  | { readonly kind: 'ok'; readonly value: T }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'forbidden'; readonly noticeCode: string | null }
  | { readonly kind: 'invalid'; readonly errors: Readonly<Record<string, readonly string[]>> }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'unavailable' };

export type Problem = Exclude<Result<never>, { kind: 'ok' }>;

export const ok = <T>(value: T): Result<T> => ({ kind: 'ok', value });
export const notFound = (): Problem => ({ kind: 'not-found' });
export const forbidden = (noticeCode: string | null = null): Problem => ({
  kind: 'forbidden',
  noticeCode,
});
export const invalid = (field: string, message: string): Problem => ({
  kind: 'invalid',
  errors: { [field]: [message] },
});
