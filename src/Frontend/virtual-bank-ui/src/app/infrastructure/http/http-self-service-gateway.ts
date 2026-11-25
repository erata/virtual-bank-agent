import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SelfServiceGateway } from '../../application/ports/self-service-gateway';
import { Account } from '../../domain/customer';
import { Result } from '../../domain/result';
import {
  BookedTransaction,
  MyBalanceOverview,
  MyProfile,
  ScheduledPayment,
  StandingOrder,
  StandingOrderInput,
  TransferInput,
} from '../../domain/self-service';
import { PendingAction } from '../../domain/actions';
import { cardCall } from './card-call';
import { call } from './call';

/** Adapter: /api/self endpoints. No identifier is ever sent; the bearer token identifies the customer. */
@Injectable()
export class HttpSelfServiceGateway extends SelfServiceGateway {
  private readonly http = inject(HttpClient);

  profile(): Promise<Result<MyProfile>> {
    return call(this.http.get<MyProfile>('/api/self/profile'));
  }

  accounts(): Promise<Result<readonly Account[]>> {
    return call(this.http.get<Account[]>('/api/self/accounts'));
  }

  balances(): Promise<Result<MyBalanceOverview>> {
    return call(this.http.get<MyBalanceOverview>('/api/self/balances'));
  }

  upcomingPayments(days: number): Promise<Result<readonly ScheduledPayment[]>> {
    return call(
      this.http.get<ScheduledPayment[]>('/api/self/payments/upcoming', { params: { days } }),
    );
  }

  recentTransactions(days: number): Promise<Result<readonly BookedTransaction[]>> {
    return call(
      this.http.get<BookedTransaction[]>('/api/self/transactions/recent', { params: { days } }),
    );
  }

  standingOrders(): Promise<Result<readonly StandingOrder[]>> {
    return call(this.http.get<StandingOrder[]>('/api/self/standing-orders'));
  }

  prepareTransfer(transfer: TransferInput): Promise<Result<PendingAction>> {
    return cardCall(this.http.post<unknown>('/api/self/transfers', transfer));
  }

  prepareInternalTransfer(
    fromAccountId: string,
    toAccountId: string,
    amount: number,
  ): Promise<Result<PendingAction>> {
    return cardCall(
      this.http.post<unknown>('/api/self/internal-transfers', {
        fromAccountId,
        toAccountId,
        amount,
      }),
    );
  }

  prepareCreateStandingOrder(order: StandingOrderInput): Promise<Result<PendingAction>> {
    return cardCall(this.http.post<unknown>('/api/self/standing-orders', order));
  }

  prepareCancelPayment(paymentId: string): Promise<Result<PendingAction>> {
    return cardCall(
      this.http.post<unknown>(
        `/api/self/payments/${encodeURIComponent(paymentId)}/cancellation`,
        {},
      ),
    );
  }

  prepareStandingOrder(orderId: string, active: boolean): Promise<Result<PendingAction>> {
    return cardCall(
      this.http.post<unknown>(
        `/api/self/standing-orders/${encodeURIComponent(orderId)}/${active ? 'resume' : 'pause'}`,
        {},
      ),
    );
  }
}
