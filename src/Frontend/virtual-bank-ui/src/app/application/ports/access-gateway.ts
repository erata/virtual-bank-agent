import { Result } from '../../domain/result';

/** Port to GET /api/access: the caller's own effective permissions. */
export abstract class AccessGateway {
  abstract permissions(): Promise<Result<readonly string[]>>;
}
