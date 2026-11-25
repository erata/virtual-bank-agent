import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import { AuthSession } from '../../application/ports/auth-session';
import { AuthState } from '../../domain/identity';
import { toIdentity } from './claims';

const RETURN_URL_KEY = 'virtual-bank.return-url';

/** Adapter: implements the {@link AuthSession} port with angular-auth-oidc-client. */
@Injectable()
export class OidcAuthSession extends AuthSession {
  private readonly oidc = inject(OidcSecurityService);
  private readonly router = inject(Router);
  private readonly current = signal<AuthState>({ status: 'checking' });

  readonly state = this.current.asReadonly();

  /** Runs once at startup: completes a pending login redirect (code exchange) or restores the session. */
  async initialize(): Promise<void> {
    try {
      const response = await firstValueFrom(this.oidc.checkAuth());
      await this.refreshState(response.isAuthenticated);
      if (response.isAuthenticated) {
        const returnUrl = sessionStorage.getItem(RETURN_URL_KEY);
        sessionStorage.removeItem(RETURN_URL_KEY);
        if (returnUrl?.startsWith('/') && !returnUrl.startsWith('//')) {
          await this.router.navigateByUrl(returnUrl);
        }
      }
    } catch {
      this.current.set({ status: 'anonymous' });
    }

    // Keep the state in sync with renewals, expiry and logout in another tab.
    this.oidc.isAuthenticated$.subscribe(
      ({ isAuthenticated }) => void this.refreshState(isAuthenticated),
    );
  }

  login(returnUrl?: string): void {
    // Only same-application paths are remembered (no open redirect).
    if (returnUrl?.startsWith('/') && !returnUrl.startsWith('//')) {
      sessionStorage.setItem(RETURN_URL_KEY, returnUrl);
    }
    this.oidc.authorize();
  }

  logout(): void {
    this.current.set({ status: 'anonymous' });
    this.oidc.logoffAndRevokeTokens().subscribe({ error: () => this.oidc.logoffLocal() });
  }

  private async refreshState(isAuthenticated: boolean): Promise<void> {
    if (!isAuthenticated) {
      this.current.set({ status: 'anonymous' });
      return;
    }

    const claims = (await firstValueFrom(this.oidc.getPayloadFromAccessToken())) as Record<
      string,
      unknown
    > | null;
    this.current.set(
      claims ? { status: 'authenticated', identity: toIdentity(claims) } : { status: 'anonymous' },
    );
  }
}
