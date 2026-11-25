import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CustomerGateway } from '../../application/ports/customer-gateway';
import { Account, CustomerDetail, CustomerSummary } from '../../domain/customer';
import { Result } from '../../domain/result';
import { PendingAction } from '../../domain/actions';
import { call } from './call';
import { cardCall } from './card-call';

/** Adapter: customer endpoints of the API over HTTP (same origin, proxied by nginx). */
@Injectable()
export class HttpCustomerGateway extends CustomerGateway {
  private readonly http = inject(HttpClient);

  list(): Promise<Result<readonly CustomerSummary[]>> {
    return call(this.http.get<CustomerSummary[]>('/api/customers'));
  }

  get(customerId: string): Promise<Result<CustomerDetail>> {
    return call(this.http.get<CustomerDetail>(`/api/customers/${encodeURIComponent(customerId)}`));
  }

  accounts(customerId: string): Promise<Result<readonly Account[]>> {
    return call(
      this.http.get<Account[]>(`/api/customers/${encodeURIComponent(customerId)}/accounts`),
    );
  }

  prepareCash(
    customerId: string,
    accountId: string,
    amount: number,
    deposit: boolean,
  ): Promise<Result<PendingAction>> {
    return cardCall(
      this.http.post<unknown>(
        `/api/customers/${encodeURIComponent(customerId)}/accounts/${encodeURIComponent(accountId)}/${deposit ? 'deposits' : 'withdrawals'}`,
        { amount },
      ),
    );
  }
}
