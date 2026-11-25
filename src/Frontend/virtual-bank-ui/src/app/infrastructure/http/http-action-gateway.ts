import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { ActionGateway } from '../../application/ports/action-gateway';
import { ActionOutcome } from '../../domain/actions';
import { Result } from '../../domain/result';
import { call } from './call';

/** Adapter: POST /api/actions/{id}/confirm and DELETE /api/actions/{id}. */
@Injectable()
export class HttpActionGateway extends ActionGateway {
  private readonly http = inject(HttpClient);

  confirm(actionId: string): Promise<Result<ActionOutcome>> {
    return call(
      this.http.post<ActionOutcome>(`/api/actions/${encodeURIComponent(actionId)}/confirm`, {}),
    );
  }

  discard(actionId: string): Promise<Result<void>> {
    return call(
      this.http.delete(`/api/actions/${encodeURIComponent(actionId)}`).pipe(map(() => undefined)),
    );
  }
}
