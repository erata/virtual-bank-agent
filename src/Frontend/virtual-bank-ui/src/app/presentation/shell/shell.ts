import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthSession } from '../../application/ports/auth-session';
import { AccessNavigation } from '../../application/use-cases/access-navigation';
import { LANGUAGES, isLanguage } from '../../domain/language';
import { I18n } from '../i18n/i18n';
import { MessageKey } from '../i18n/messages';

/** Application frame: navigation, language choice, signed-in user, sign in / sign out. */
@Component({
  selector: 'ba-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="topbar">
      <a routerLink="/" class="brand" [title]="i18n.t('app.tenantTitle')">
        <span class="brand-name">VirtualBank</span>
        <span class="brand-tag">{{ i18n.t('app.tenant') }}</span>
      </a>
      @if (identity(); as user) {
        <nav aria-label="Main">
          <!-- Built from the caller's effective permissions (GET /api/access); the API still checks every call. -->
          @for (section of navigation.sections(); track section.path) {
            <a [routerLink]="section.path" routerLinkActive="active">{{ navLabel(section.key) }}</a>
          }
        </nav>
      }
      <div class="session">
        <select
          class="language-select"
          [attr.aria-label]="i18n.t('app.language')"
          [value]="i18n.language()"
          (change)="choose($any($event.target).value)"
          data-testid="language"
        >
          @for (l of languages; track l.code) {
            <option [value]="l.code" [selected]="l.code === i18n.language()">{{ l.name }}</option>
          }
        </select>
        @if (identity(); as user) {
          <div class="user-menu">
            <button
              type="button"
              class="user-button"
              (click)="toggleMenu($event)"
              [attr.aria-expanded]="menuOpen()"
              aria-haspopup="menu"
              data-testid="current-user"
            >
              <span class="avatar" aria-hidden="true">{{ initials(user.displayName) }}</span>
              {{ user.displayName }} <span aria-hidden="true">▾</span>
            </button>
            @if (menuOpen()) {
              <!-- Invisible layer behind the menu: a click anywhere outside closes it. -->
              <div
                class="menu-backdrop"
                (click)="menuOpen.set(false)"
                data-testid="menu-backdrop"
              ></div>
              <div class="user-dropdown" role="menu" data-testid="user-menu">
                <div class="user-dropdown-head">
                  <strong>{{ user.displayName }}</strong>
                  <span class="muted small">{{ user.username }}</span>
                </div>
                <a role="menuitem" routerLink="/me" (click)="menuOpen.set(false)">{{
                  i18n.t('nav.me')
                }}</a>
                <button
                  type="button"
                  role="menuitem"
                  class="menu-item"
                  (click)="auth.logout()"
                  data-testid="logout"
                >
                  {{ i18n.t('app.signOut') }}
                </button>
              </div>
            }
          </div>
        } @else if (auth.state().status === 'anonymous') {
          <button type="button" (click)="auth.login()" data-testid="login">
            {{ i18n.t('app.signIn') }}
          </button>
        }
      </div>
    </header>
    <main class="content">
      @if (auth.state().status === 'checking') {
        <p class="muted">{{ i18n.t('app.checking') }}</p>
      } @else {
        <router-outlet />
      }
    </main>
    <footer class="footer muted">{{ i18n.t('app.footer') }}</footer>
  `,
})
export class Shell {
  protected readonly auth = inject(AuthSession);
  protected readonly navigation = inject(AccessNavigation);
  protected readonly i18n = inject(I18n);
  protected readonly languages = LANGUAGES;

  protected readonly identity = computed(() => {
    const state = this.auth.state();
    return state.status === 'authenticated' ? state.identity : null;
  });

  constructor() {
    // Reload permissions whenever the signed-in subject changes (sign-in, sign-out, user switch).
    effect(() => void this.navigation.loadFor(this.identity()?.subject ?? null));
  }

  protected readonly menuOpen = signal(false);

  protected toggleMenu(event: Event): void {
    event.stopPropagation();
    this.menuOpen.update((open) => !open);
  }

  @HostListener('document:keydown.escape')
  protected closeOnEscape(): void {
    this.menuOpen.set(false);
  }

  protected initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0]!.toUpperCase())
      .join('');
  }

  protected navLabel(key: string): string {
    return this.i18n.t(`nav.${key}` as MessageKey);
  }

  protected choose(value: string): void {
    if (isLanguage(value)) this.i18n.use(value);
  }
}
