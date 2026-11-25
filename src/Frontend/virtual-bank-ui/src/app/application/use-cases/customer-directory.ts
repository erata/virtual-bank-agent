import { Injectable, inject } from '@angular/core';
import { Account, CustomerDetail, CustomerSummary, isCustomerId } from '../../domain/customer';
import { PendingAction } from '../../domain/actions';
import { Result, invalid, notFound } from '../../domain/result';
import { CustomerGateway } from '../ports/customer-gateway';

/**
 * Customer use cases. Input that cannot identify a customer is answered locally with "not found",
 * exactly like the API would, without a network call.
 */
@Injectable({ providedIn: 'root' })
export class CustomerDirectory {
  private readonly gateway = inject(CustomerGateway);

  listCustomers(): Promise<Result<readonly CustomerSummary[]>> {
    return this.gateway.list();
  }

  async getCustomer(customerId: string): Promise<Result<CustomerDetail>> {
    return isCustomerId(customerId) ? this.gateway.get(customerId) : notFound();
  }

  async listAccounts(customerId: string): Promise<Result<readonly Account[]>> {
    return isCustomerId(customerId) ? this.gateway.accounts(customerId) : notFound();
  }

  /** Branch cash desk: amount bounds mirror the API (0.01 .. 10,000); the API checks branch and balance. */
  async prepareCash(
    customerId: string,
    accountId: string,
    amount: number,
    deposit: boolean,
  ): Promise<Result<PendingAction>> {
    if (!isCustomerId(customerId) || !/^A-[0-9]{5}$/.test(accountId)) return notFound();
    if (!(amount > 0 && amount <= 10000))
      return invalid('amount', 'Enter an amount between 0.01 and 10,000.');
    return this.gateway.prepareCash(customerId, accountId, amount, deposit);
  }
}
