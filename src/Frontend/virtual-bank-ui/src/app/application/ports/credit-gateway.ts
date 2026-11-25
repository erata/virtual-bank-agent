import { PendingAction } from '../../domain/actions';
import { CreditApplication, CreditNote, CreditNoteEntry } from '../../domain/credit-application';
import { Result } from '../../domain/result';

/**
 * Port to the credit endpoints of the API. Approval is two-step by design (M6): prepare returns a single-use
 * confirmation without changing anything; only confirming it executes. There is no one-step approve operation.
 */
export abstract class CreditGateway {
  abstract list(): Promise<Result<readonly CreditApplication[]>>;

  abstract get(applicationId: string): Promise<Result<CreditApplication>>;

  abstract addNote(applicationId: string, text: string): Promise<Result<CreditNote>>;

  abstract notes(applicationId: string): Promise<Result<readonly CreditNoteEntry[]>>;

  /** Prepares a decision card; nothing changes until the card is confirmed via the action gateway. */
  abstract prepareApproval(
    applicationId: string,
    comment: string | null,
  ): Promise<Result<PendingAction>>;

  abstract prepareRejection(applicationId: string, reason: string): Promise<Result<PendingAction>>;

  /** Bank customer applies for a credit (credit.request); confirming submits it for an approver's decision. */
  abstract prepareCreditRequest(
    amount: number,
    termMonths: number,
    purpose: string,
  ): Promise<Result<PendingAction>>;
}
