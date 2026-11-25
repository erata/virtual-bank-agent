import { Injectable, inject } from '@angular/core';
import {
  CREDIT_NOTE_MAX_LENGTH,
  CreditNoteEntry,
  DECISION_NOTE_MAX_LENGTH,
  CreditApplication,
  CreditNote,
  isCreditApplicationId,
  normalizeNoteText,
} from '../../domain/credit-application';
import { Result, invalid, notFound } from '../../domain/result';
import { PendingAction } from '../../domain/actions';
import { CreditGateway } from '../ports/credit-gateway';

/** Credit queries and commands. Validation mirrors the backend so users get feedback before a round trip. */
@Injectable({ providedIn: 'root' })
export class CreditWorkspace {
  private readonly gateway = inject(CreditGateway);

  listApplications(): Promise<Result<readonly CreditApplication[]>> {
    return this.gateway.list();
  }

  async getApplication(applicationId: string): Promise<Result<CreditApplication>> {
    return isCreditApplicationId(applicationId) ? this.gateway.get(applicationId) : notFound();
  }

  async notes(applicationId: string): Promise<Result<readonly CreditNoteEntry[]>> {
    return isCreditApplicationId(applicationId) ? this.gateway.notes(applicationId) : notFound();
  }

  /** A bank customer applies for a credit; bounds mirror the API (500..250,000 EUR, 6..360 months). */
  async prepareCreditRequest(
    amount: number,
    termMonths: number,
    purpose: string,
  ): Promise<Result<PendingAction>> {
    const text = purpose.trim();
    if (!(amount >= 500 && amount <= 250000))
      return invalid('amount', 'Enter an amount between 500 and 250,000 EUR.');
    if (!(Number.isInteger(termMonths) && termMonths >= 6 && termMonths <= 360))
      return invalid('termMonths', 'Choose a term between 6 and 360 months.');
    if (text.length < 3 || text.length > 160)
      return invalid('purpose', 'Describe the purpose in 3 to 160 characters.');
    return this.gateway.prepareCreditRequest(amount, termMonths, text);
  }

  /** Prepares an approval card (nothing changes). An optional comment is stored with the decision. */
  async prepareApproval(applicationId: string, comment: string): Promise<Result<PendingAction>> {
    const note = comment.trim();
    if (note.length > DECISION_NOTE_MAX_LENGTH)
      return invalid('note', `At most ${DECISION_NOTE_MAX_LENGTH} characters.`);
    return isCreditApplicationId(applicationId)
      ? this.gateway.prepareApproval(applicationId, note || null)
      : notFound();
  }

  /** Prepares a rejection card; a reason is required (same rule as the API). */
  async prepareRejection(applicationId: string, reason: string): Promise<Result<PendingAction>> {
    const note = reason.trim();
    if (note.length === 0 || note.length > DECISION_NOTE_MAX_LENGTH)
      return invalid(
        'note',
        `A rejection needs a reason (1 to ${DECISION_NOTE_MAX_LENGTH} characters).`,
      );
    return isCreditApplicationId(applicationId)
      ? this.gateway.prepareRejection(applicationId, note)
      : notFound();
  }

  async addNote(applicationId: string, rawText: string): Promise<Result<CreditNote>> {
    const text = normalizeNoteText(rawText);
    if (text === null) {
      return invalid('text', `A note must contain 1 to ${CREDIT_NOTE_MAX_LENGTH} characters.`);
    }

    return isCreditApplicationId(applicationId)
      ? this.gateway.addNote(applicationId, text)
      : notFound();
  }
}
