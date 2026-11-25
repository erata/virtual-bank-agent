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

/** Port to /api/self: the caller's own banking data, identified by the access token only. */
export abstract class SelfServiceGateway {
  abstract profile(): Promise<Result<MyProfile>>;

  abstract accounts(): Promise<Result<readonly Account[]>>;

  abstract balances(): Promise<Result<MyBalanceOverview>>;

  abstract upcomingPayments(days: number): Promise<Result<readonly ScheduledPayment[]>>;

  abstract recentTransactions(days: number): Promise<Result<readonly BookedTransaction[]>>;

  abstract standingOrders(): Promise<Result<readonly StandingOrder[]>>;

  /** The following only PREPARE a confirmation card; nothing changes until the card is confirmed. */
  abstract prepareTransfer(transfer: TransferInput): Promise<Result<PendingAction>>;

  abstract prepareCancelPayment(paymentId: string): Promise<Result<PendingAction>>;

  abstract prepareInternalTransfer(
    fromAccountId: string,
    toAccountId: string,
    amount: number,
  ): Promise<Result<PendingAction>>;

  abstract prepareCreateStandingOrder(order: StandingOrderInput): Promise<Result<PendingAction>>;

  abstract prepareStandingOrder(orderId: string, active: boolean): Promise<Result<PendingAction>>;
}
