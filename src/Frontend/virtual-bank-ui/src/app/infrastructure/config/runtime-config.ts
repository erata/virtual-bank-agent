import { InjectionToken } from '@angular/core';

/**
 * Deployment settings read at startup from `/config.json` (generated from environment variables in the
 * container), so one build runs against any Keycloak realm or host. Nothing here is a secret: the UI is a
 * public OIDC client using Authorization Code + PKCE.
 */
export interface RuntimeConfig {
  /** Browser-facing Keycloak base URL, e.g. http://localhost:8080 */
  readonly keycloakUrl: string;
  readonly realm: string;
  readonly clientId: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');

const SAFE_SEGMENT = /^[A-Za-z0-9._-]+$/;

/** Validates untrusted JSON; throws with a readable message instead of starting a half-configured app. */
export function parseRuntimeConfig(raw: unknown): RuntimeConfig {
  if (typeof raw !== 'object' || raw === null) {
    throw new Error('config.json must contain a JSON object.');
  }

  const value = raw as Record<string, unknown>;
  const text = (key: string): string => {
    const field = value[key];
    if (typeof field !== 'string' || field.trim() === '') {
      throw new Error(`config.json: '${key}' is required.`);
    }
    return field.trim();
  };

  const keycloakUrl = text('keycloakUrl').replace(/\/+$/, '');
  let url: URL;
  try {
    url = new URL(keycloakUrl);
  } catch {
    throw new Error("config.json: 'keycloakUrl' must be an absolute URL.");
  }
  if (
    url.protocol !== 'https:' &&
    !(url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname))
  ) {
    throw new Error(
      "config.json: 'keycloakUrl' must use https (plain http is allowed for localhost only).",
    );
  }

  const realm = text('realm');
  const clientId = text('clientId');
  if (!SAFE_SEGMENT.test(realm) || !SAFE_SEGMENT.test(clientId)) {
    throw new Error(
      "config.json: 'realm' and 'clientId' may contain letters, digits, '.', '_' and '-' only.",
    );
  }

  return { keycloakUrl, realm, clientId };
}

export function issuerOf(config: RuntimeConfig): string {
  return `${config.keycloakUrl}/realms/${config.realm}`;
}
