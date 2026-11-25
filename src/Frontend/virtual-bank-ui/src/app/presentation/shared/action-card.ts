import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { ActionCards } from '../../application/use-cases/action-cards';
import { ActionOutcome, PendingAction, secondsLeft } from '../../domain/actions';
import { Problem } from '../../domain/result';
import { I18n } from '../i18n/i18n';
import { MessageKey } from '../i18n/messages';
import { ProblemView } from './problem-view';

/**
 * Confirmation card for a prepared change, used by the menu screens AND inside assistant answers. Nothing has
 * happened while the card is shown; "Confirm" is the only way to execute it (the API re-checks everything then).
 * Values are interpolated as text.
 */
@Component({
  selector: 'ba-action-card',
  imports: [ProblemView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="confirm-card"
      [class.done]="state() !== 'open'"
      role="group"
      [attr.aria-label]="title()"
      data-testid="action-card"
    >
      <h3>{{ title() }}</h3>
      @switch (state()) {
        @case ('open') {
          <p class="muted">{{ i18n.t('action.lead') }}</p>
          <dl class="facts">
            @for (f of action().facts; track f.key) {
              <dt>{{ factLabel(f.key) }}</dt>
              <dd>{{ factValue(f.key, f.value) }}</dd>
            }
          </dl>
          <div class="doc-actions">
            <button
              type="button"
              (click)="confirm()"
              [disabled]="busy() || remaining() <= 0"
              data-testid="action-confirm"
            >
              {{ i18n.t(confirmKey()) }}
            </button>
            <button
              type="button"
              class="secondary"
              (click)="discard()"
              [disabled]="busy()"
              data-testid="action-discard"
            >
              {{ i18n.t('common.cancel') }}
            </button>
            <span class="muted small">
              {{
                remaining() > 0
                  ? i18n.t('action.expires', { s: remaining() })
                  : i18n.t('action.expired')
              }}
            </span>
          </div>
          @if (problem(); as p) {
            <ba-problem [problem]="p" />
          }
        }
        @case ('done') {
          <div class="notice notice-ok" role="status" data-testid="action-done">
            {{ doneText() }}
            @for (f of outcomeFacts(); track $index) {
              <div class="small">{{ factLabel(f.key) }}: {{ factValue(f.key, f.value) }}</div>
            }
          </div>
        }
        @case ('discarded') {
          <p class="muted" data-testid="action-discarded">{{ i18n.t('action.discarded') }}</p>
        }
      }
    </div>
  `,
})
export class ActionCard {
  private readonly cards = inject(ActionCards);
  protected readonly i18n = inject(I18n);

  readonly action = input.required<PendingAction>();
  readonly completed = output<ActionOutcome>();
  readonly dismissed = output<void>();

  protected readonly state = signal<'open' | 'done' | 'discarded'>('open');
  protected readonly busy = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  private readonly outcome = signal<ActionOutcome | null>(null);
  private readonly now = signal(Date.now());

  protected readonly remaining = computed(() => secondsLeft(this.action(), this.now()));
  protected readonly title = computed(() =>
    this.i18n.t(`action.kind.${this.action().kind}` as MessageKey),
  );
  protected readonly confirmKey = computed(
    () => `action.confirm.${this.action().kind}` as MessageKey,
  );
  /** Outcome-specific text first (e.g. a transfer that was BOOKED vs. PLANNED), then the kind's general text. */
  protected readonly doneText = computed(() => {
    const o = this.outcome();
    if (!o) return '';
    const specific = this.i18n.t(`action.done.${o.kind}.${o.outcome}` as MessageKey, {
      id: o.resourceId,
    });
    return specific.startsWith('action.done.')
      ? this.i18n.t(`action.done.${o.kind}` as MessageKey, { id: o.resourceId })
      : specific;
  });
  protected readonly outcomeFacts = computed(() =>
    (this.outcome()?.facts ?? []).filter((f) => f.key !== 'status'),
  );

  constructor() {
    // A new card in the same place (e.g. the next order after a confirmed one) starts fresh, never as "done".
    effect(() => {
      this.action();
      untracked(() => {
        this.state.set('open');
        this.problem.set(null);
        this.outcome.set(null);
      });
    });
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected factLabel(key: string): string {
    const label = this.i18n.t(`action.fact.${key}` as MessageKey);
    return label.startsWith('action.fact.') ? key : label;
  }

  /** The credit "checks" fact is a fixed code list from the API; everything else is shown verbatim. */
  protected factValue(key: string, value: string): string {
    if (key === 'mode' && (value === 'immediate' || value === 'planned'))
      return this.i18n.t(`action.mode.${value}`);
    if (key === 'beneficiaryBank' && (value === 'internal' || value === 'external'))
      return this.i18n.t(`action.bank.${value}`);
    if (key === 'frequency' && (value === 'weekly' || value === 'monthly'))
      return this.i18n.t(`bank.${value}`);
    return key === 'checks'
      ? '✓ ' +
          (['approval.checkState', 'approval.checkLimit', 'approval.checkFourEyes'] as const)
            .map((k) => this.i18n.t(k))
            .join(' · ✓ ')
      : value;
  }

  protected async confirm(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    const result = await this.cards.confirm(this.action().id);
    this.busy.set(false);
    if (result.kind === 'ok') {
      this.outcome.set(result.value);
      this.state.set('done');
      this.completed.emit(result.value);
    } else {
      // Expired, replayed, changed meanwhile or refused by the database: nothing was executed.
      this.problem.set(result);
    }
  }

  protected async discard(): Promise<void> {
    this.busy.set(true);
    await this.cards.discard(this.action().id);
    this.busy.set(false);
    this.state.set('discarded');
    this.dismissed.emit();
  }
}
