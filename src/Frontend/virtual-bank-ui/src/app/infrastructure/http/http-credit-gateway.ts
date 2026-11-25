import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CreditGateway } from '../../application/ports/credit-gateway';
import { PendingAction } from '../../domain/actions';
import { cardCall } from './card-call';
import { CreditApplication, CreditNote, CreditNoteEntry } from '../../domain/credit-application';
import { Result } from '../../domain/result';
import { call } from './call';

/** Adapter: credit endpoints of the API over HTTP (same origin, proxied by nginx). */
@Injectable()
export class HttpCreditGateway extends CreditGateway {
  private readonly http = inject(HttpClient);

  list(): Promise<Result<readonly CreditApplication[]>> {
    return call(this.http.get<CreditApplication[]>('/api/credit/applications'));
  }

  get(applicationId: string): Promise<Result<CreditApplication>> {
    return call(
      this.http.get<CreditApplication>(
        `/api/credit/applications/${encodeURIComponent(applicationId)}`,
      ),
    );
  }

  notes(applicationId: string): Promise<Result<readonly CreditNoteEntry[]>> {
    return call(
      this.http.get<CreditNoteEntry[]>(
        `/api/credit/applications/${encodeURIComponent(applicationId)}/notes`,
      ),
    );
  }

  prepareApproval(applicationId: string, comment: string | null): Promise<Result<PendingAction>> {
    return this.card(
      `/api/credit/applications/${encodeURIComponent(applicationId)}/approval`,
      comment,
    );
  }

  prepareRejection(applicationId: string, reason: string): Promise<Result<PendingAction>> {
    return this.card(
      `/api/credit/applications/${encodeURIComponent(applicationId)}/rejection`,
      reason,
    );
  }

  prepareCreditRequest(
    amount: number,
    termMonths: number,
    purpose: string,
  ): Promise<Result<PendingAction>> {
    return cardCall(
      this.http.post<unknown>('/api/credit/requests', { amount, termMonths, purpose }),
    );
  }

  private card(url: string, note: string | null): Promise<Result<PendingAction>> {
    return cardCall(this.http.post<unknown>(url, { note }));
  }

  addNote(applicationId: string, text: string): Promise<Result<CreditNote>> {
    return call(
      this.http.post<CreditNote>(
        `/api/credit/applications/${encodeURIComponent(applicationId)}/notes`,
        { text },
      ),
    );
  }
}
