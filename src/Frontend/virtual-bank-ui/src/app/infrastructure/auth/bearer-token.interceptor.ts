import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { first, switchMap } from 'rxjs';

/**
 * Attaches the access token ONLY to same-origin API calls (relative URLs under /api/). Absolute URLs —
 * including Keycloak itself and any third party — never receive the token.
 */
export function isApiRequest(url: string): boolean {
  return url.startsWith('/api/') && !url.startsWith('//');
}

export const bearerTokenInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiRequest(request.url)) {
    return next(request);
  }

  return inject(OidcSecurityService)
    .getAccessToken()
    .pipe(
      first(),
      switchMap((token) =>
        next(token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request),
      ),
    );
};
