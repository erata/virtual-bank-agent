import { Injectable, inject } from '@angular/core';
import { Account } from '../../domain/customer';
import { PendingAction } from '../../domain/actions';
import { Result, invalid, notFound } from '../../domain/result';
import {
  BookedTransaction,
  MyBalanceOverview,
  MyProfile,
  ScheduledPayment,
  StandingOrder,
  StandingOrderInput,
  TransferInput,
  transferProblems,
} from '../../domain/self-service';
import { SelfServiceGateway } from '../ports/self-service-gateway';

/** Self-service banking for the signed-in customer. Day ranges are clamped to a sane window. */
@Injectable({ providedIn: 'root' })
export class MyBanking {
  private readonly gateway = inject(SelfServiceGateway);

  profile(): Promise<Result<MyProfile>> {
    return this.gateway.profile();
  }

  accounts(): Promise<Result<readonly Account[]>> {
    return this.gateway.accounts();
  }

  balances(): Promise<Result<MyBalanceOverview>> {
    return this.gateway.balances();
  }

  standingOrders(): Promise<Result<readonly StandingOrder[]>> {
    return this.gateway.standingOrders();
  }

  /** Prepares a transfer card after the same checks the API makes (nothing is sent yet). */
  async prepareTransfer(transfer: TransferInput, today: string): Promise<Result<PendingAction>> {
    const normalized = {
      ...transfer,
      iban: transfer.iban.replace(/\s+/g, '').toUpperCase(),
      beneficiaryName: transfer.beneficiaryName.trim(),
      reference: transfer.reference.trim(),
    };
    const problems = transferProblems(normalized, today);
    return problems.length > 0
      ? invalid(problems[0], problems.join(','))
      : this.gateway.prepareTransfer(normalized);
  }

  async prepareInternalTransfer(
    fromAccountId: string,
    toAccountId: string,
    amount: number,
  ): Promise<Result<PendingAction>> {
    if (!ACCOUNT_ID.test(fromAccountId) || !ACCOUNT_ID.test(toAccountId)) return notFound();
    if (fromAccountId === toAccountId)
      return invalid('toAccountId', 'Choose two different accounts.');
    if (!(amount > 0 && amount <= 10000))
      return invalid('amount', 'Enter an amount between 0.01 and 10,000.');
    return this.gateway.prepareInternalTransfer(fromAccountId, toAccountId, amount);
  }

  async prepareCreateStandingOrder(
    order: StandingOrderInput,
    today: string,
  ): Promise<Result<PendingAction>> {
    const normalized = {
      ...order,
      iban: order.iban.replace(/\s+/g, '').toUpperCase(),
      beneficiaryName: order.beneficiaryName.trim(),
      reference: order.reference.trim(),
    };
    const problems = transferProblems(
      { ...normalized, executionDate: normalized.firstDate },
      today,
    );
    return problems.length > 0
      ? invalid(problems[0], problems.join(','))
      : this.gateway.prepareCreateStandingOrder(normalized);
  }

  async prepareCancelPayment(paymentId: string): Promise<Result<PendingAction>> {
    return PAYMENT_ID.test(paymentId) ? this.gateway.prepareCancelPayment(paymentId) : notFound();
  }

  async prepareStandingOrder(orderId: string, active: boolean): Promise<Result<PendingAction>> {
    return STANDING_ORDER_ID.test(orderId)
      ? this.gateway.prepareStandingOrder(orderId, active)
      : notFound();
  }

  upcomingPayments(days = 30): Promise<Result<readonly ScheduledPayment[]>> {
    return this.gateway.upcomingPayments(clampDays(days));
  }

  recentTransactions(days = 30): Promise<Result<readonly BookedTransaction[]>> {
    return this.gateway.recentTransactions(clampDays(days));
  }
}

const PAYMENT_ID = /^P-[0-9]{4}-[A-Z0-9]{2,8}$/;
const STANDING_ORDER_ID = /^SO-[0-9]{4}-[A-Z0-9]{2,8}$/;
const ACCOUNT_ID = /^A-[0-9]{5}$/;

function clampDays(days: number): number {
  return Math.min(90, Math.max(1, Math.trunc(days)));
}
