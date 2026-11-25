import { ActionOutcome } from '../../domain/actions';
import { Result } from '../../domain/result';

/** Port to /api/actions: the single place where a prepared change is executed (or discarded) by its owner. */
export abstract class ActionGateway {
  abstract confirm(actionId: string): Promise<Result<ActionOutcome>>;

  abstract discard(actionId: string): Promise<Result<void>>;
}
