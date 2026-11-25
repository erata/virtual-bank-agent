import { Observable, map } from 'rxjs';
import { PendingAction, parsePendingAction } from '../../domain/actions';
import { Result } from '../../domain/result';
import { call } from './call';

/** Runs a "prepare" request and validates the returned confirmation card; a malformed card is an error. */
export function cardCall(request: Observable<unknown>): Promise<Result<PendingAction>> {
  return call(
    request.pipe(
      map((body) => {
        const card = parsePendingAction(body);
        if (!card) throw new Error('Malformed confirmation card');
        return card;
      }),
    ),
  );
}
