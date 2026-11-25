import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthSession } from '../../application/ports/auth-session';

/**
 * Sends anonymous users to the identity provider. This is navigation convenience, not security:
 * the APIs reject requests without a valid token regardless of what the UI shows.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthSession);
  if (auth.state().status === 'authenticated') {
    return true;
  }

  auth.login(state.url);
  return false;
};
