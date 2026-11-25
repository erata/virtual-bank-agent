import { Injectable, inject } from '@angular/core';
import { ActionOutcome, isActionId } from '../../domain/actions';
import { Result, notFound } from '../../domain/result';
import { ActionGateway } from '../ports/action-gateway';

/** Confirm or discard a prepared change. Malformed ids never reach the API. */
@Injectable({ providedIn: 'root' })
export class ActionCards {
  private readonly gateway = inject(ActionGateway);

  async confirm(actionId: string): Promise<Result<ActionOutcome>> {
    return isActionId(actionId) ? this.gateway.confirm(actionId) : notFound();
  }

  async discard(actionId: string): Promise<Result<void>> {
    return isActionId(actionId) ? this.gateway.discard(actionId) : notFound();
  }
}
