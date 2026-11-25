import { PendingAction } from '../../domain/actions';
import { Account, CustomerDetail, CustomerSummary } from '../../domain/customer';
import { Result } from '../../domain/result';

/** Port to the customer endpoints of the API. Implementations translate transport details into {@link Result}. */
export abstract class CustomerGateway {
  abstract list(): Promise<Result<readonly CustomerSummary[]>>;

  abstract get(customerId: string): Promise<Result<CustomerDetail>>;

  abstract accounts(customerId: string): Promise<Result<readonly Account[]>>;

  /** Branch cash desk (account.cash): prepares a deposit/withdrawal card; nothing is booked yet. */
  abstract prepareCash(
    customerId: string,
    accountId: string,
    amount: number,
    deposit: boolean,
  ): Promise<Result<PendingAction>>;
}
