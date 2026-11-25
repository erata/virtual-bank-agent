import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { CreditWorkspace } from '../../../application/use-cases/credit-workspace';
import { ActionOutcome, PendingAction } from '../../../domain/actions';
import {
  CREDIT_NOTE_MAX_LENGTH,
  CreditApplication,
  CreditNoteEntry,
  DECISION_NOTE_MAX_LENGTH,
} from '../../../domain/credit-application';
import { Problem } from '../../../domain/result';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';
import { ActionCard } from '../../shared/action-card';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';

/**
 * One credit application with everything the caller may do on it:
 * approvers decide (approve / reject with reason) through a confirmation card, analysts add notes,
 * everybody who can read it sees the notes, including decision notes. Roles that cannot decide are told why.
 */
@Component({
  selector: 'ba-credit-application-detail-page',
  imports: [RouterLink, CurrencyPipe, DatePipe, FormsModule, ProblemView, ActionCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>
      <a routerLink="/credit">{{ i18n.t('credit.back') }}</a>
    </p>
    @if (application.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (application.problem(); as problem) {
      <h1>{{ applicationId() }}</h1>
      <ba-problem [problem]="problem" />
    } @else if (application.value(); as a) {
      <h1>{{ i18n.t('credit.detailTitle', { id: a.id }) }}</h1>
      <dl class="facts">
        <dt>{{ i18n.t('common.customer') }}</dt>
        <dd>
          <a [routerLink]="['/customers', a.customerId]">{{ a.customerId }}</a>
        </dd>
        <dt>{{ i18n.t('common.amount') }}</dt>
        <dd>{{ a.amount | currency: a.currency }}</dd>
        <dt>{{ i18n.t('common.status') }}</dt>
        <dd>
          <span class="badge" [attr.data-status]="a.status" data-testid="credit-status">{{
            statusLabel(a.status)
          }}</span>
        </dd>
        <dt>{{ i18n.t('credit.purpose') }}</dt>
        <dd>{{ a.purpose }}</dd>
        <dt>{{ i18n.t('common.version') }}</dt>
        <dd>{{ a.version }}</dd>
      </dl>

      <section class="approval" data-testid="decision">
        <h2>{{ i18n.t('approval.title') }}</h2>
        @if (a.status !== 'SUBMITTED') {
          <p class="muted">{{ i18n.t('decision.closed', { status: statusLabel(a.status) }) }}</p>
        } @else if (!canDecide()) {
          <p class="muted" data-testid="decision-not-allowed">
            {{ i18n.t('decision.notYourRole') }}
          </p>
        } @else if (card(); as c) {
          <ba-action-card [action]="c" (completed)="decided($event)" (dismissed)="card.set(null)" />
        } @else {
          <p class="muted">{{ i18n.t('approval.lead') }}</p>
          <label class="field">
            <span>{{ i18n.t('decision.note') }}</span>
            <textarea
              rows="2"
              [maxlength]="decisionMax"
              [(ngModel)]="decisionNote"
              name="decisionNote"
              [placeholder]="i18n.t('decision.notePlaceholder')"
              data-testid="decision-note"
            ></textarea>
          </label>
          <div class="doc-actions">
            <button
              type="button"
              (click)="prepare(a, 'approve')"
              [disabled]="busy()"
              data-testid="approval-prepare"
            >
              {{ i18n.t('approval.review') }}
            </button>
            <button
              type="button"
              class="danger"
              (click)="prepare(a, 'reject')"
              [disabled]="busy()"
              data-testid="rejection-prepare"
            >
              {{ i18n.t('decision.reviewReject') }}
            </button>
          </div>
        }
        @if (decisionDone(); as message) {
          <div class="notice notice-ok" role="status" data-testid="action-done">{{ message }}</div>
        }
        @if (decisionProblem(); as problem) {
          <ba-problem [problem]="problem" />
        }
      </section>

      <h2>{{ i18n.t('notes.title') }}</h2>
      @if (notes.value(); as list) {
        <ul class="notes" data-testid="credit-notes">
          @for (n of list; track n.id) {
            <li [class.decision-note]="n.body.startsWith('[')">
              <span class="muted small"
                >{{ n.createdAt | date: 'yyyy-MM-dd HH:mm' }} ·
                {{ n.byYou ? i18n.t('notes.you') : i18n.t('notes.colleague') }}</span
              >
              <div>{{ n.body }}</div>
            </li>
          } @empty {
            <li class="muted">{{ i18n.t('notes.none') }}</li>
          }
        </ul>
      }

      <h3>{{ i18n.t('credit.addNote') }}</h3>
      @if (!canWriteNotes()) {
        <!-- The form stays: the API, not the UI, decides; its answer is shown if someone without note rights tries. -->
        <p class="muted" data-testid="note-hint">{{ i18n.t('notes.noWriteRight') }}</p>
      }
      <form (ngSubmit)="addNote()" class="note-form">
        <textarea
          name="text"
          rows="3"
          [maxlength]="maxLength"
          [(ngModel)]="noteText"
          [disabled]="saving()"
          [placeholder]="i18n.t('credit.notePlaceholder')"
          data-testid="note-text"
        ></textarea>
        <div class="row">
          <span class="muted">{{ noteText.length }} / {{ maxLength }}</span>
          <button type="submit" [disabled]="saving()" data-testid="note-submit">
            {{ i18n.t('credit.saveNote') }}
          </button>
        </div>
      </form>
      @if (noteProblem(); as problem) {
        <ba-problem [problem]="problem" />
      }
      @if (savedNoteId(); as id) {
        <div class="notice notice-ok" role="status" data-testid="note-saved">
          {{ i18n.t('credit.noteSaved', { id: id }) }}
        </div>
      }
    }
  `,
})
export class CreditApplicationDetailPage implements OnInit {
  /** Bound from the route parameter :applicationId. */
  readonly applicationId = input.required<string>();

  private readonly workspace = inject(CreditWorkspace);
  private readonly access = inject(AccessNavigation);
  protected readonly i18n = inject(I18n);

  protected readonly maxLength = CREDIT_NOTE_MAX_LENGTH;
  protected readonly decisionMax = DECISION_NOTE_MAX_LENGTH;
  protected readonly application = new AsyncResult<CreditApplication>();
  protected readonly notes = new AsyncResult<readonly CreditNoteEntry[]>();
  protected readonly saving = signal(false);
  protected readonly noteProblem = signal<Problem | null>(null);
  protected readonly savedNoteId = signal<number | null>(null);
  protected noteText = '';
  protected decisionNote = '';

  /** UI hints only; the API decides (permission, limit, four-eyes, state) when preparing AND when confirming. */
  protected readonly canDecide = computed(() => this.access.granted().includes('credit.approve'));
  protected readonly canWriteNotes = computed(() =>
    this.access.granted().includes('credit.note.write'),
  );
  protected readonly card = signal<PendingAction | null>(null);
  protected readonly decisionProblem = signal<Problem | null>(null);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    void this.reload();
  }

  private async reload(): Promise<void> {
    await this.application.load(() => this.workspace.getApplication(this.applicationId()));
    if (this.application.value())
      void this.notes.load(() => this.workspace.notes(this.applicationId()));
  }

  protected statusLabel(status: string): string {
    return this.i18n.t(`credit.status.${status}` as MessageKey);
  }

  protected async prepare(a: CreditApplication, decision: 'approve' | 'reject'): Promise<void> {
    this.busy.set(true);
    this.decisionProblem.set(null);
    const result =
      decision === 'approve'
        ? await this.workspace.prepareApproval(a.id, this.decisionNote)
        : await this.workspace.prepareRejection(a.id, this.decisionNote);
    this.busy.set(false);
    if (result.kind === 'ok') this.card.set(result.value);
    else this.decisionProblem.set(result);
  }

  protected readonly decisionDone = signal<string | null>(null);

  protected async decided(outcome: ActionOutcome): Promise<void> {
    // Close the card before reloading; the page re-renders and must not show an executed card as "open" again.
    this.card.set(null);
    this.decisionDone.set(
      this.i18n.t(`action.done.${outcome.kind}` as MessageKey, { id: outcome.resourceId }),
    );
    this.decisionNote = '';
    // After a rejection the approver may no longer read the application (it is neither SUBMITTED nor approved by
    // them); the card already shows the outcome, so a "not found" here is expected and not an error.
    const result = await this.workspace.getApplication(this.applicationId());
    if (result.kind === 'ok') await this.reload();
  }

  protected async addNote(): Promise<void> {
    this.saving.set(true);
    this.noteProblem.set(null);
    this.savedNoteId.set(null);
    const result = await this.workspace.addNote(this.applicationId(), this.noteText);
    this.saving.set(false);
    if (result.kind === 'ok') {
      this.savedNoteId.set(result.value.id);
      this.noteText = '';
      void this.notes.load(() => this.workspace.notes(this.applicationId()));
    } else {
      this.noteProblem.set(result);
    }
  }
}
