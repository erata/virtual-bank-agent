import { LogLevel, OpenIdConfiguration } from 'angular-auth-oidc-client';
import { RuntimeConfig, issuerOf } from '../config/runtime-config';

/**
 * Authorization Code + PKCE (S256) against Keycloak, renewed with refresh tokens (no hidden iframes,
 * no third-party cookies). Tokens stay in session storage of this origin and are sent only to same-origin
 * /api/ routes by the bearer interceptor.
 */
export function oidcConfiguration(config: RuntimeConfig, origin: string): OpenIdConfiguration {
  return {
    authority: issuerOf(config),
    clientId: config.clientId,
    redirectUrl: `${origin}/`,
    postLogoutRedirectUri: `${origin}/`,
    responseType: 'code',
    scope: 'openid profile',
    useRefreshToken: true,
    silentRenew: true,
    renewTimeBeforeTokenExpiresInSeconds: 30,
    ignoreNonceAfterRefresh: true,
    triggerRefreshWhenIdTokenExpired: false,
    startCheckSession: false,
    autoUserInfo: false,
    historyCleanupOff: false,
    logLevel: LogLevel.Warn,
  };
}
