import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { AuthSession } from '../../../application/ports/auth-session';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { I18n } from '../../i18n/i18n';

/** What the access token says about the user, plus the permissions the API resolved — transparency only. */
@Component({
  selector: 'ba-profile-page',
  imports: [DatePipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('me.title') }}</h1>
    @if (identity(); as me) {
      <dl class="facts" data-testid="identity">
        <dt>{{ i18n.t('home.user') }}</dt>
        <dd>{{ me.username }}</dd>
        <dt>{{ i18n.t('me.subject') }}</dt>
        <dd>
          <code>{{ me.subject }}</code>
        </dd>
        <dt>{{ i18n.t('me.roles') }}</dt>
        <dd>{{ me.roles.join(', ') || '—' }}</dd>
        <dt>{{ i18n.t('me.groups') }}</dt>
        <dd>{{ me.groups.join(', ') || '—' }}</dd>
        <dt>{{ i18n.t('common.branch') }}</dt>
        <dd data-testid="branch">{{ me.branch ?? '—' }}</dd>
        <dt>{{ i18n.t('me.department') }}</dt>
        <dd>{{ me.department ?? '—' }}</dd>
        <dt>{{ i18n.t('me.limit') }}</dt>
        <dd>{{ me.approvalLimit === null ? '—' : (me.approvalLimit | number) + ' EUR' }}</dd>
        <dt>{{ i18n.t('me.permissions') }}</dt>
        <dd data-testid="permissions">
          @for (p of navigation.granted(); track p) {
            <code class="permission">{{ p }}</code>
          } @empty {
            —
          }
        </dd>
        <dt>{{ i18n.t('me.expires') }}</dt>
        <dd>
          {{ me.expiresAt ? (me.expiresAt | date: 'HH:mm:ss') : '—' }} {{ i18n.t('me.renewed') }}
        </dd>
      </dl>
      <p class="muted">{{ i18n.t('me.note') }}</p>
    }
  `,
})
export class ProfilePage {
  private readonly auth = inject(AuthSession);
  protected readonly navigation = inject(AccessNavigation);
  protected readonly i18n = inject(I18n);

  protected readonly identity = computed(() => {
    const s = this.auth.state();
    return s.status === 'authenticated' ? s.identity : null;
  });
}
