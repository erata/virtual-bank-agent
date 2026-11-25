import { HttpErrorResponse } from '@angular/common/http';
import { Problem } from '../../domain/result';

const NOTICE_CODE = /^[A-Z][A-Z0-9_]{2,63}$/;

/** True for strings shaped like a deterministic notice code; anything else from the server is discarded. */
export function isNoticeCode(value: unknown): value is string {
  return typeof value === 'string' && NOTICE_CODE.test(value);
}

/**
 * Translates an HTTP failure into a transport-neutral {@link Problem}. Only the deterministic notice code is
 * taken from a 403 body (and only if it looks like one); free text from the server is never displayed.
 */
export function toProblem(error: unknown): Problem {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'unavailable' };
  }

  const body =
    typeof error.error === 'object' && error.error !== null
      ? (error.error as Record<string, unknown>)
      : {};

  switch (error.status) {
    case 401:
      return { kind: 'unauthenticated' };
    case 403: {
      const code = body['noticeCode'];
      return {
        kind: 'forbidden',
        noticeCode: isNoticeCode(code) ? code : null,
      };
    }
    case 404:
      return { kind: 'not-found' };
    case 400:
    case 422: {
      const errors = body['errors'];
      if (typeof errors === 'object' && errors !== null) {
        const fields: Record<string, readonly string[]> = {};
        for (const [field, messages] of Object.entries(errors as Record<string, unknown>)) {
          fields[field] = Array.isArray(messages)
            ? messages.filter((m): m is string => typeof m === 'string')
            : [];
        }
        return { kind: 'invalid', errors: fields };
      }
      return { kind: 'invalid', errors: {} };
    }
    default:
      return { kind: 'unavailable' };
  }
}
