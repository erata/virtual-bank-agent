import { Observable, firstValueFrom } from 'rxjs';
import { Result, ok } from '../../domain/result';
import { toProblem } from './problem-mapper';

/** Runs one HTTP request and converts success or failure into a {@link Result}. Never throws. */
export async function call<T>(request: Observable<T>): Promise<Result<T>> {
  try {
    return ok(await firstValueFrom(request));
  } catch (error) {
    return toProblem(error);
  }
}
