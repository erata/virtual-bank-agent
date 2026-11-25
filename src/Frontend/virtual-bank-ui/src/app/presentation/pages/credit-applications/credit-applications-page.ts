import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { PendingAction } from '../../../domain/actions';
import { Problem } from '../../../domain/result';
import { ActionCard } from '../../shared/action-card';
import { CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CreditWorkspace } from '../../../application/use-cases/credit-workspace';
import { CreditApplication } from '../../../domain/credit-application';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';

@Component({
  selector: 'ba-credit-applications-page',
  imports: [RouterLink, CurrencyPipe, FormsModule, ProblemView, ActionCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('credit.title') }}</h1>
    @if (canRequest()) {
      <section class="approval" data-testid="credit-request">
        <h2>{{ i18n.t('creditRequest.title') }}</h2>
        @if (card(); as c) {
          <ba-action-card [action]="c" (completed)="requested()" (dismissed)="card.set(null)" />
        } @else {
          <p class="muted">{{ i18n.t('creditRequest.lead') }}</p>
          <form class="transfer-form" (submit)="$event.preventDefault(); prepareRequest()">
            <label class="field"
              ><span>{{ i18n.t('common.amount') }} (EUR)</span>
              <input
                class="filter"
                name="amount"
                type="number"
                min="500"
                max="250000"
                step="100"
                [(ngModel)]="request.amount"
                data-testid="request-amount"
            /></label>
            <label class="field"
              ><span>{{ i18n.t('creditRequest.term') }}</span>
              <input
                class="filter"
                name="term"
                type="number"
                min="6"
                max="360"
                step="1"
                [(ngModel)]="request.termMonths"
                data-testid="request-term"
            /></label>
            <label class="field"
              ><span>{{ i18n.t('credit.purpose') }}</span>
              <input
                class="filter"
                name="purpose"
                maxlength="160"
                [(ngModel)]="request.purpose"
                data-testid="request-purpose"
            /></label>
            <div class="doc-actions">
              <button type="submit" [disabled]="busy()" data-testid="request-prepare">
                {{ i18n.t('creditRequest.review') }}
              </button>
            </div>
          </form>
        }
        @if (requestProblem(); as problem) {
          <ba-problem [problem]="problem" />
        }
      </section>
    }
    @if (applications.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (applications.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (applications.value(); as list) {
      <p class="muted">{{ i18n.t('credit.count', { n: list.length }) }}</p>
      <table data-testid="credit-table">
        <thead>
          <tr>
            <th>{{ i18n.t('common.id') }}</th>
            <th>{{ i18n.t('common.customer') }}</th>
            <th class="number">{{ i18n.t('common.amount') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
            <th>{{ i18n.t('credit.purpose') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (a of list; track a.id) {
            <tr>
              <td>
                <a [routerLink]="['/credit', a.id]">{{ a.id }}</a>
              </td>
              <td>{{ a.customerId }}</td>
              <td class="number">{{ a.amount | currency: a.currency : 'symbol' : '1.0-0' }}</td>
              <td>
                <span class="badge" [attr.data-status]="a.status">{{ statusLabel(a.status) }}</span>
              </td>
              <td>{{ a.purpose }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="5" class="muted">{{ i18n.t('credit.none') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class CreditApplicationsPage implements OnInit {
  protected readonly i18n = inject(I18n);

  private readonly workspace = inject(CreditWorkspace);

  protected readonly applications = new AsyncResult<readonly CreditApplication[]>();

  protected statusLabel(status: string): string {
    return this.i18n.t(`credit.status.${status}` as MessageKey);
  }

  private readonly access = inject(AccessNavigation);
  /** Bank customers (credit.request) apply here and see their own applications in the list below. */
  protected readonly canRequest = computed(() => this.access.granted().includes('credit.request'));
  protected readonly card = signal<PendingAction | null>(null);
  protected readonly requestProblem = signal<Problem | null>(null);
  protected readonly busy = signal(false);
  protected request = {
    amount: null as number | null,
    termMonths: 60 as number | null,
    purpose: '',
  };

  protected async prepareRequest(): Promise<void> {
    this.busy.set(true);
    this.requestProblem.set(null);
    const result = await this.workspace.prepareCreditRequest(
      Number(this.request.amount ?? 0),
      Number(this.request.termMonths ?? 0),
      this.request.purpose,
    );
    this.busy.set(false);
    if (result.kind === 'ok') this.card.set(result.value);
    else this.requestProblem.set(result);
  }

  protected requested(): void {
    this.request = { amount: null, termMonths: 60, purpose: '' };
    void this.applications.load(() => this.workspace.listApplications());
  }

  ngOnInit(): void {
    void this.applications.load(() => this.workspace.listApplications());
  }
}
