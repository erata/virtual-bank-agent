import { Signal } from '@angular/core';
import { AuthState } from '../../domain/identity';

/**
 * Port: the user's session with the identity provider. The application and presentation layers depend on
 * this abstraction only; the OIDC library lives behind an adapter in the infrastructure layer.
 */
export abstract class AuthSession {
  abstract readonly state: Signal<AuthState>;

  /** Redirects to the identity provider; returns to `returnUrl` afterwards. */
  abstract login(returnUrl?: string): void;

  /** Ends the local and the identity-provider session. */
  abstract logout(): void;
}
