import { Identity } from '../../domain/identity';

/** Keycloak's own default roles, hidden from the UI to keep the identity card readable. */
const TECHNICAL_ROLES = new Set(['offline_access', 'uma_authorization']);

function text(claims: Record<string, unknown>, name: string): string | null {
  const value = claims[name];
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

/**
 * Maps access-token claims to the display model. Tolerant on purpose: malformed claims become "not set",
 * because this model never grants anything (the APIs re-evaluate the token themselves).
 */
export function toIdentity(claims: Record<string, unknown>): Identity {
  const realmAccess = claims['realm_access'];
  const roles =
    typeof realmAccess === 'object' &&
    realmAccess !== null &&
    Array.isArray((realmAccess as { roles?: unknown }).roles)
      ? (realmAccess as { roles: unknown[] }).roles.filter(
          (r): r is string =>
            typeof r === 'string' && !TECHNICAL_ROLES.has(r) && !r.startsWith('default-roles-'),
        )
      : [];

  const groups = Array.isArray(claims['groups'])
    ? claims['groups'].filter((g): g is string => typeof g === 'string')
    : [];

  const limitText = text(claims, 'approval_limit');
  const limit =
    limitText !== null && /^[0-9]+(\.[0-9]+)?$/.test(limitText) ? Number(limitText) : null;

  const exp = claims['exp'];
  const username = text(claims, 'preferred_username') ?? text(claims, 'sub') ?? 'unknown';
  const fullName = [text(claims, 'given_name'), text(claims, 'family_name')]
    .filter(Boolean)
    .join(' ');

  return {
    subject: text(claims, 'sub') ?? '',
    username,
    displayName: text(claims, 'name') ?? (fullName || username),
    roles: [...roles].sort(),
    groups: [...groups].sort(),
    branch: text(claims, 'branch'),
    department: text(claims, 'department'),
    approvalLimit: limit,
    expiresAt: typeof exp === 'number' ? new Date(exp * 1000) : null,
  };
}
