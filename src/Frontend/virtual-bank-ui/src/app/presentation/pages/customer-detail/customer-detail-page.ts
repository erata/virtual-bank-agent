import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { ActionOutcome, PendingAction } from '../../../domain/actions';
import { MessageKey } from '../../i18n/messages';
import { Problem } from '../../../domain/result';
import { ActionCard } from '../../shared/action-card';
import { CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { CustomerDirectory } from '../../../application/use-cases/customer-directory';
import { Account, CustomerDetail } from '../../../domain/customer';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';

@Component({
  selector: 'ba-customer-detail-page',
  imports: [RouterLink, CurrencyPipe, FormsModule, ProblemView, ActionCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>
      <a routerLink="/customers">{{ i18n.t('customer.back') }}</a>
    </p>
    @if (customer.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (customer.problem(); as problem) {
      <h1>{{ customerId() }}</h1>
      <ba-problem [problem]="problem" />
    } @else if (customer.value(); as c) {
      <h1>
        {{ c.firstName }} {{ c.lastName }} <span class="muted">{{ c.id }}</span>
      </h1>
      @if (c.sensitiveFieldsMasked) {
        <div class="notice" data-testid="masked">
          {{ i18n.t('customer.masked') }}
        </div>
      }
      <dl class="facts">
        <dt>{{ i18n.t('common.branch') }}</dt>
        <dd>{{ c.branch }}</dd>
        <dt>{{ i18n.t('customers.segment') }}</dt>
        <dd>{{ c.segment }}</dd>
        <dt>{{ i18n.t('customer.email') }}</dt>
        <dd>{{ c.email ?? '••••' }}</dd>
        <dt>{{ i18n.t('customer.phone') }}</dt>
        <dd>{{ c.phone ?? '••••' }}</dd>
        <dt>IBAN</dt>
        <dd>{{ c.iban ?? '••••' }}</dd>
        <dt>{{ i18n.t('customer.dob') }}</dt>
        <dd>{{ c.dateOfBirth ?? '••••' }}</dd>
      </dl>

      <h2>{{ i18n.t('customer.accounts') }}</h2>
      @if (accounts.problem(); as problem) {
        <ba-problem [problem]="problem" />
      } @else if (accounts.value(); as list) {
        <table>
          <thead>
            <tr>
              <th>{{ i18n.t('customer.account') }}</th>
              <th>{{ i18n.t('common.type') }}</th>
              <th class="number">{{ i18n.t('common.balance') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (a of list; track a.id) {
              <tr>
                <td>{{ a.id }}</td>
                <td>{{ a.type }}</td>
                <td class="number">{{ a.balance | currency: a.currency }}</td>
              </tr>
            } @empty {
              <tr>
                <td colspan="3" class="muted">{{ i18n.t('customer.noAccounts') }}</td>
              </tr>
            }
          </tbody>
        </table>
      }

      @if (canCash() && (accounts.value() ?? []).length > 0) {
        <section class="approval" data-testid="cash-desk">
          <h2>{{ i18n.t('cash.title') }}</h2>
          @if (card(); as c) {
            <ba-action-card
              [action]="c"
              (completed)="booked($event)"
              (dismissed)="card.set(null)"
            />
          } @else {
            <p class="muted">{{ i18n.t('cash.lead') }}</p>
            <form class="transfer-form" (submit)="$event.preventDefault()">
              <label class="field"
                ><span>{{ i18n.t('customer.account') }}</span>
                <select
                  class="filter"
                  name="cashAccount"
                  [(ngModel)]="cashAccount"
                  data-testid="cash-account"
                >
                  @for (a of accounts.value() ?? []; track a.id) {
                    <option [value]="a.id">
                      {{ a.id }} · {{ a.type }} · {{ a.balance | currency: a.currency }}
                    </option>
                  }
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('common.amount') }}</span>
                <input
                  class="filter"
                  name="cashAmount"
                  type="number"
                  min="0.01"
                  max="10000"
                  step="0.01"
                  [(ngModel)]="cashAmount"
                  data-testid="cash-amount"
              /></label>
              <div class="doc-actions">
                <button
                  type="button"
                  (click)="prepareCash(true)"
                  [disabled]="busy()"
                  data-testid="cash-deposit"
                >
                  {{ i18n.t('cash.deposit') }}
                </button>
                <button
                  type="button"
                  class="secondary"
                  (click)="prepareCash(false)"
                  [disabled]="busy()"
                  data-testid="cash-withdrawal"
                >
                  {{ i18n.t('cash.withdraw') }}
                </button>
              </div>
            </form>
          }
          @if (cashDone(); as message) {
            <div class="notice notice-ok" role="status" data-testid="action-done">
              {{ message }}
            </div>
          }
          @if (cashProblem(); as problem) {
            <ba-problem [problem]="problem" />
          }
        </section>
      }
    }
  `,
})
export class CustomerDetailPage implements OnInit {
  protected readonly i18n = inject(I18n);

  /** Bound from the route parameter :customerId. */
  readonly customerId = input.required<string>();

  private readonly directory = inject(CustomerDirectory);

  protected readonly customer = new AsyncResult<CustomerDetail>();

  protected readonly accounts = new AsyncResult<readonly Account[]>();

  private readonly access = inject(AccessNavigation);
  /** UI hint only; the API and RLS require account.cash, customer.read and the same branch on prepare AND confirm. */
  protected readonly canCash = computed(() => this.access.granted().includes('account.cash'));
  protected readonly card = signal<PendingAction | null>(null);
  protected readonly cashProblem = signal<Problem | null>(null);
  protected readonly busy = signal(false);
  protected cashAccount = '';
  protected cashAmount: number | null = null;

  protected async prepareCash(deposit: boolean): Promise<void> {
    this.busy.set(true);
    this.cashProblem.set(null);
    this.cashDone.set(null);
    const account = this.cashAccount || this.accounts.value()?.[0]?.id || '';
    const result = await this.directory.prepareCash(
      this.customerId(),
      account,
      Number(this.cashAmount ?? 0),
      deposit,
    );
    this.busy.set(false);
    if (result.kind === 'ok') this.card.set(result.value);
    else this.cashProblem.set(result);
  }

  /** After a confirmed booking the balances shown above are refreshed. */
  protected readonly cashDone = signal<string | null>(null);

  protected booked(outcome: ActionOutcome): void {
    // Close the card first: reloading the accounts re-renders this section, and a re-created card must not
    // reappear as "open" for an action that has already been executed.
    this.card.set(null);
    this.cashDone.set(
      this.i18n.t(`action.done.${outcome.kind}` as MessageKey, { id: outcome.resourceId }) +
        ' ' +
        outcome.facts.map((f) => f.value).join(' · '),
    );
    this.cashAmount = null;
    void this.accounts.load(() => this.directory.listAccounts(this.customerId()));
  }

  ngOnInit(): void {
    const id = this.customerId();
    void this.customer
      .load(() => this.directory.getCustomer(id))
      .then(() => {
        if (this.customer.value()) {
          void this.accounts.load(() => this.directory.listAccounts(id));
        }
      });
  }
}
