import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { PendingAction } from '../../../domain/actions';
import { Problem, Result } from '../../../domain/result';
import { ActionCard } from '../../shared/action-card';
import { MyBanking } from '../../../application/use-cases/my-banking';
import { Account } from '../../../domain/customer';
import {
  BookedTransaction,
  MyBalanceOverview,
  MyProfile,
  ScheduledPayment,
  StandingOrder,
} from '../../../domain/self-service';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';

/** The signed-in bank customer's own banking data. Each section is a separate, authorized API call. */
@Component({
  selector: 'ba-my-banking-page',
  imports: [DecimalPipe, FormsModule, ProblemView, ActionCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('bank.title') }}</h1>

    @if (profile.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (profile.value(); as me) {
      <dl class="facts" data-testid="my-profile">
        <dt>{{ i18n.t('common.customer') }}</dt>
        <dd>{{ me.firstName }} {{ me.lastName }} ({{ me.id }})</dd>
        <dt>{{ i18n.t('common.branch') }}</dt>
        <dd>{{ me.branch }}</dd>
        <dt>IBAN</dt>
        <dd>{{ me.iban ?? i18n.t('common.masked') }}</dd>
        <dt>{{ i18n.t('customer.email') }}</dt>
        <dd>{{ me.email ?? i18n.t('common.masked') }}</dd>
      </dl>
    }

    <h2>{{ i18n.t('bank.balances') }}</h2>
    @if (balances.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (balances.value(); as b) {
      <table data-testid="my-balances">
        <thead>
          <tr>
            <th>{{ i18n.t('common.currency') }}</th>
            <th>{{ i18n.t('bank.accounts') }}</th>
            <th class="number">{{ i18n.t('common.balance') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (t of b.totalsByCurrency; track t.currency) {
            <tr>
              <td>{{ t.currency }}</td>
              <td>{{ t.accountCount }}</td>
              <td class="number">{{ t.balance | number: '1.2-2' }}</td>
            </tr>
          }
        </tbody>
      </table>
      <p class="muted">{{ i18n.t('bank.noCurrencyMix') }}</p>
    }

    @if (canOrder()) {
      <section class="approval" data-testid="orders">
        <h2>{{ i18n.t('bank.orders') }}</h2>
        @if (card(); as c) {
          <ba-action-card [action]="c" (completed)="ordered()" (dismissed)="card.set(null)" />
        } @else {
          <div class="segmented" role="tablist" style="margin-bottom: 0.75rem">
            <button
              type="button"
              [class.active]="orderType() === 'sepa'"
              (click)="orderType.set('sepa')"
              data-testid="order-sepa"
            >
              {{ i18n.t('bank.newTransfer') }}
            </button>
            <button
              type="button"
              [class.active]="orderType() === 'internal'"
              (click)="orderType.set('internal')"
              data-testid="order-internal"
            >
              {{ i18n.t('bank.internalTransfer') }}
            </button>
            <button
              type="button"
              [class.active]="orderType() === 'standing'"
              (click)="orderType.set('standing')"
              data-testid="order-standing"
            >
              {{ i18n.t('bank.newStandingOrder') }}
            </button>
          </div>
          @if (orderType() === 'internal') {
            <form class="transfer-form" (submit)="$event.preventDefault(); prepareInternal()">
              <label class="field"
                ><span>{{ i18n.t('action.fact.fromAccount') }}</span>
                <select
                  class="filter"
                  name="from"
                  [(ngModel)]="internal.from"
                  data-testid="internal-from"
                >
                  @for (a of accounts.value() ?? []; track a.id) {
                    <option [value]="a.id">
                      {{ a.id }} · {{ a.type }} · {{ a.balance | number: '1.2-2' }} {{ a.currency }}
                    </option>
                  }
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('action.fact.toAccount') }}</span>
                <select
                  class="filter"
                  name="to"
                  [(ngModel)]="internal.to"
                  data-testid="internal-to"
                >
                  @for (a of accounts.value() ?? []; track a.id) {
                    <option [value]="a.id">{{ a.id }} · {{ a.type }} · {{ a.currency }}</option>
                  }
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('common.amount') }}</span>
                <input
                  class="filter"
                  name="amount"
                  type="number"
                  min="0.01"
                  max="10000"
                  step="0.01"
                  [(ngModel)]="internal.amount"
                  data-testid="internal-amount"
              /></label>
              <div class="doc-actions">
                <button type="submit" [disabled]="busy()" data-testid="internal-prepare">
                  {{ i18n.t('bank.reviewTransfer') }}
                </button>
                <span class="muted small">{{ i18n.t('bank.internalNote') }}</span>
              </div>
            </form>
          } @else if (orderType() === 'standing') {
            <form class="transfer-form" (submit)="$event.preventDefault(); prepareStanding()">
              <label class="field"
                ><span>{{ i18n.t('action.fact.beneficiary') }}</span>
                <input
                  class="filter"
                  name="soBeneficiary"
                  [(ngModel)]="standingInput.beneficiaryName"
                  maxlength="70"
                  data-testid="standing-beneficiary"
              /></label>
              <label class="field"
                ><span>IBAN</span>
                <input
                  class="filter"
                  name="soIban"
                  [(ngModel)]="standingInput.iban"
                  maxlength="42"
                  data-testid="standing-iban"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('common.amount') }}</span>
                <input
                  class="filter"
                  name="soAmount"
                  type="number"
                  min="0.01"
                  max="10000"
                  step="0.01"
                  [(ngModel)]="standingInput.amount"
                  data-testid="standing-amount"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('common.currency') }}</span>
                <select class="filter" name="soCurrency" [(ngModel)]="standingInput.currency">
                  @for (c of currencies(); track c) {
                    <option [value]="c">{{ c }}</option>
                  }
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('bank.frequency') }}</span>
                <select
                  class="filter"
                  name="soFrequency"
                  [(ngModel)]="standingInput.frequency"
                  data-testid="standing-frequency"
                >
                  <option value="monthly">{{ i18n.t('bank.monthly') }}</option>
                  <option value="weekly">{{ i18n.t('bank.weekly') }}</option>
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('action.fact.nextDate') }}</span>
                <input
                  class="filter"
                  name="soFirst"
                  type="date"
                  [min]="today"
                  [(ngModel)]="standingInput.firstDate"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('action.fact.reference') }}</span>
                <input
                  class="filter"
                  name="soReference"
                  [(ngModel)]="standingInput.reference"
                  maxlength="140"
              /></label>
              <div class="doc-actions">
                <button type="submit" [disabled]="busy()" data-testid="standing-prepare">
                  {{ i18n.t('bank.reviewStandingOrder') }}
                </button>
              </div>
            </form>
          } @else {
            <form class="transfer-form" (submit)="$event.preventDefault(); prepareTransfer()">
              <label class="field"
                ><span>{{ i18n.t('action.fact.beneficiary') }}</span>
                <input
                  class="filter"
                  name="beneficiary"
                  [(ngModel)]="transfer.beneficiaryName"
                  maxlength="70"
                  required
                  data-testid="transfer-beneficiary"
              /></label>
              <label class="field"
                ><span>IBAN</span>
                <input
                  class="filter"
                  name="iban"
                  [(ngModel)]="transfer.iban"
                  maxlength="42"
                  placeholder="DE89 3704 0044 0532 0130 00"
                  required
                  data-testid="transfer-iban"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('common.amount') }}</span>
                <input
                  class="filter"
                  name="amount"
                  type="number"
                  min="0.01"
                  max="10000"
                  step="0.01"
                  [(ngModel)]="transfer.amount"
                  required
                  data-testid="transfer-amount"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('common.currency') }}</span>
                <select
                  class="filter"
                  name="currency"
                  [(ngModel)]="transfer.currency"
                  data-testid="transfer-currency"
                >
                  @for (c of currencies(); track c) {
                    <option [value]="c">{{ c }}</option>
                  }
                </select></label
              >
              <label class="field"
                ><span>{{ i18n.t('action.fact.executionDate') }}</span>
                <input
                  class="filter"
                  name="date"
                  type="date"
                  [min]="today"
                  [(ngModel)]="transfer.executionDate"
                  data-testid="transfer-date"
              /></label>
              <label class="field"
                ><span>{{ i18n.t('action.fact.reference') }}</span>
                <input
                  class="filter"
                  name="reference"
                  [(ngModel)]="transfer.reference"
                  maxlength="140"
                  data-testid="transfer-reference"
              /></label>
              <div class="doc-actions">
                <button type="submit" [disabled]="busy()" data-testid="transfer-prepare">
                  {{ i18n.t('bank.reviewTransfer') }}
                </button>
                <span class="muted small">{{ i18n.t('bank.transferNote') }}</span>
              </div>
            </form>
          }
        }
        @if (orderProblem(); as problem) {
          <ba-problem [problem]="problem" />
        }
      </section>
    }

    <h2>{{ i18n.t('bank.accounts') }}</h2>
    @if (accounts.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (accounts.value(); as list) {
      <table data-testid="my-accounts">
        <thead>
          <tr>
            <th>{{ i18n.t('customer.account') }}</th>
            <th>{{ i18n.t('common.type') }}</th>
            <th>{{ i18n.t('common.currency') }}</th>
            <th class="number">{{ i18n.t('common.balance') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (a of list; track a.id) {
            <tr>
              <td>{{ a.id }}</td>
              <td>{{ a.type }}</td>
              <td>{{ a.currency }}</td>
              <td class="number">{{ a.balance | number: '1.2-2' }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="4" class="muted">{{ i18n.t('customer.noAccounts') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }

    <h2>{{ i18n.t('bank.upcoming') }}</h2>
    @if (upcoming.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (upcoming.value(); as list) {
      <table data-testid="my-upcoming">
        <thead>
          <tr>
            <th>{{ i18n.t('bank.due') }}</th>
            <th>{{ i18n.t('common.id') }}</th>
            <th>{{ i18n.t('common.description') }}</th>
            <th>{{ i18n.t('common.status') }}</th>
            <th class="number">{{ i18n.t('common.amount') }}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (p of list; track p.id) {
            <tr>
              <td>{{ p.dueDate }}</td>
              <td>
                <code>{{ p.id }}</code>
              </td>
              <td>{{ p.description }}</td>
              <td>{{ p.status }}</td>
              <td class="number">{{ p.amount | number: '1.2-2' }} {{ p.currency }}</td>
              <td>
                @if (canOrder() && p.status === 'planned') {
                  <button
                    type="button"
                    class="secondary small-button"
                    (click)="cancelPayment(p.id)"
                    [disabled]="busy()"
                    data-testid="payment-cancel"
                  >
                    {{ i18n.t('bank.cancelPayment') }}
                  </button>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="muted">{{ i18n.t('bank.noPayments') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }

    <h2>{{ i18n.t('bank.recent') }}</h2>
    @if (recent.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (recent.value(); as list) {
      <table data-testid="my-transactions">
        <thead>
          <tr>
            <th>{{ i18n.t('bank.booked') }}</th>
            <th>{{ i18n.t('common.description') }}</th>
            <th class="number">{{ i18n.t('common.amount') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (t of list; track t.id) {
            <tr>
              <td>{{ t.bookedOn }}</td>
              <td>{{ t.description }}</td>
              <td class="number">{{ t.amount | number: '1.2-2' }} {{ t.currency }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="3" class="muted">{{ i18n.t('bank.noTransactions') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }

    <h2>{{ i18n.t('bank.standing') }}</h2>
    @if (standing.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (standing.value(); as list) {
      <table data-testid="my-standing-orders">
        <thead>
          <tr>
            <th>{{ i18n.t('bank.next') }}</th>
            <th>{{ i18n.t('common.description') }}</th>
            <th>{{ i18n.t('bank.frequency') }}</th>
            <th>{{ i18n.t('bank.active') }}</th>
            <th class="number">{{ i18n.t('common.amount') }}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (s of list; track s.id) {
            <tr [class.muted]="!s.active">
              <td>{{ s.nextDueDate }}</td>
              <td>{{ s.description }}</td>
              <td>{{ s.frequency }}</td>
              <td>{{ s.active ? i18n.t('common.yes') : i18n.t('bank.paused') }}</td>
              <td class="number">{{ s.amount | number: '1.2-2' }} {{ s.currency }}</td>
              <td>
                @if (canOrder()) {
                  <button
                    type="button"
                    class="secondary small-button"
                    (click)="toggleStanding(s.id, !s.active)"
                    [disabled]="busy()"
                    data-testid="standing-toggle"
                  >
                    {{ s.active ? i18n.t('bank.pause') : i18n.t('bank.resume') }}
                  </button>
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="muted">{{ i18n.t('bank.noStanding') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class MyBankingPage implements OnInit {
  protected readonly i18n = inject(I18n);

  private readonly banking = inject(MyBanking);
  private readonly access = inject(AccessNavigation);

  /** UI hint only; the API and RLS require customer.payment.write and ownership on prepare AND confirm. */
  protected readonly canOrder = computed(() =>
    this.access.granted().includes('customer.payment.write'),
  );
  protected readonly card = signal<PendingAction | null>(null);
  protected readonly orderProblem = signal<Problem | null>(null);
  protected readonly busy = signal(false);
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly currencies = computed(() => [
    ...new Set((this.accounts.value() ?? []).map((a) => a.currency)),
  ]);
  protected readonly orderType = signal<'sepa' | 'internal' | 'standing'>('sepa');
  protected internal = { from: '', to: '', amount: null as number | null };
  protected standingInput = {
    beneficiaryName: '',
    iban: '',
    amount: null as number | null,
    currency: 'EUR',
    frequency: 'monthly' as 'monthly' | 'weekly',
    firstDate: '',
    reference: '',
  };

  protected async prepareInternal(): Promise<void> {
    const list = this.accounts.value() ?? [];
    await this.prepare(() =>
      this.banking.prepareInternalTransfer(
        this.internal.from || list[0]?.id || '',
        this.internal.to || list[1]?.id || '',
        Number(this.internal.amount ?? 0),
      ),
    );
  }

  protected async prepareStanding(): Promise<void> {
    await this.prepare(() =>
      this.banking.prepareCreateStandingOrder(
        {
          beneficiaryName: this.standingInput.beneficiaryName,
          iban: this.standingInput.iban,
          amount: Number(this.standingInput.amount ?? 0),
          currency: this.standingInput.currency,
          frequency: this.standingInput.frequency,
          firstDate: this.standingInput.firstDate || null,
          reference: this.standingInput.reference,
        },
        this.today,
      ),
    );
  }

  protected transfer = {
    beneficiaryName: '',
    iban: '',
    amount: null as number | null,
    currency: 'EUR',
    executionDate: '',
    reference: '',
  };

  protected async prepareTransfer(): Promise<void> {
    await this.prepare(() =>
      this.banking.prepareTransfer(
        {
          beneficiaryName: this.transfer.beneficiaryName,
          iban: this.transfer.iban,
          amount: Number(this.transfer.amount ?? 0),
          currency: this.transfer.currency,
          executionDate: this.transfer.executionDate || null,
          reference: this.transfer.reference,
        },
        this.today,
      ),
    );
  }

  protected cancelPayment(id: string): Promise<void> {
    return this.prepare(() => this.banking.prepareCancelPayment(id));
  }

  protected toggleStanding(id: string, active: boolean): Promise<void> {
    return this.prepare(() => this.banking.prepareStandingOrder(id, active));
  }

  private async prepare(run: () => Promise<Result<PendingAction>>): Promise<void> {
    this.busy.set(true);
    this.orderProblem.set(null);
    const result = await run();
    this.busy.set(false);
    if (result.kind === 'ok') {
      this.card.set(result.value);
      document
        .querySelector('[data-testid=orders]')
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    } else {
      this.orderProblem.set(result);
    }
  }

  /** After a confirmed order, refresh what it affects. The card itself shows the outcome. */
  protected ordered(): void {
    this.transfer = {
      ...this.transfer,
      beneficiaryName: '',
      iban: '',
      amount: null,
      reference: '',
    };
    this.internal = { ...this.internal, amount: null };
    this.standingInput = {
      ...this.standingInput,
      beneficiaryName: '',
      iban: '',
      amount: null,
      reference: '',
    };
    // Bookings change balances and history immediately; planned orders show up under upcoming payments.
    void this.balances.load(() => this.banking.balances());
    void this.accounts.load(() => this.banking.accounts());
    void this.recent.load(() => this.banking.recentTransactions(30));
    void this.upcoming.load(() => this.banking.upcomingPayments(30));
    void this.standing.load(() => this.banking.standingOrders());
  }

  protected readonly profile = new AsyncResult<MyProfile>();
  protected readonly balances = new AsyncResult<MyBalanceOverview>();
  protected readonly accounts = new AsyncResult<readonly Account[]>();
  protected readonly upcoming = new AsyncResult<readonly ScheduledPayment[]>();
  protected readonly recent = new AsyncResult<readonly BookedTransaction[]>();
  protected readonly standing = new AsyncResult<readonly StandingOrder[]>();

  ngOnInit(): void {
    void this.profile.load(() => this.banking.profile());
    void this.balances.load(() => this.banking.balances());
    void this.accounts.load(() => this.banking.accounts());
    void this.upcoming.load(() => this.banking.upcomingPayments(30));
    void this.recent.load(() => this.banking.recentTransactions(30));
    void this.standing.load(() => this.banking.standingOrders());
  }
}
