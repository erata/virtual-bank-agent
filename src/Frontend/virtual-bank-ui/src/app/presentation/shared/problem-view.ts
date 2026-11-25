import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AuthSession } from '../../application/ports/auth-session';
import { Problem } from '../../domain/result';
import { I18n } from '../i18n/i18n';
import { noticeText } from './notice-messages';

/** Renders any {@link Problem} the same way everywhere. */
@Component({
  selector: 'ba-problem',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (problem().kind) {
      @case ('forbidden') {
        <div class="notice notice-forbidden" role="alert" data-testid="notice">
          <strong>{{ i18n.t('problem.accessDenied') }}</strong> {{ forbiddenText() }}
          @if (noticeCode()) {
            <code class="notice-code">{{ noticeCode() }}</code>
          }
        </div>
      }
      @case ('not-found') {
        <div class="notice" role="status" data-testid="not-found">
          {{ i18n.t('problem.notFound') }}
        </div>
      }
      @case ('unauthenticated') {
        <div class="notice" role="alert">
          {{ i18n.t('problem.sessionExpired') }}
          <button type="button" class="link" (click)="auth.login()">
            {{ i18n.t('problem.signInAgain') }}
          </button>
        </div>
      }
      @case ('invalid') {
        <div class="notice notice-invalid" role="alert">
          @for (message of messages(); track message) {
            <div>{{ message }}</div>
          } @empty {
            {{ i18n.t('problem.invalid') }}
          }
        </div>
      }
      @default {
        <div class="notice notice-error" role="alert">{{ i18n.t('problem.unreachable') }}</div>
      }
    }
  `,
})
export class ProblemView {
  readonly problem = input.required<Problem>();

  protected readonly auth = inject(AuthSession);
  protected readonly i18n = inject(I18n);

  protected readonly noticeCode = computed(() => {
    const p = this.problem();
    return p.kind === 'forbidden' ? p.noticeCode : null;
  });

  protected readonly forbiddenText = computed(() =>
    noticeText(this.noticeCode(), this.i18n.language()),
  );

  protected readonly messages = computed(() => {
    const p = this.problem();
    return p.kind === 'invalid' ? Object.values(p.errors).flat() : [];
  });
}
