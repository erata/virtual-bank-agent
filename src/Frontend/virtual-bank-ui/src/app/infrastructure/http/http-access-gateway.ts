import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';
import { AccessGateway } from '../../application/ports/access-gateway';
import { Result } from '../../domain/result';
import { call } from './call';

const PERMISSION = /^[a-z][a-z0-9.-]{0,63}$/;

/** Adapter: GET /api/access. Values that do not look like permission IDs are dropped. */
@Injectable()
export class HttpAccessGateway extends AccessGateway {
  private readonly http = inject(HttpClient);

  permissions(): Promise<Result<readonly string[]>> {
    return call(
      this.http.get<unknown>('/api/access').pipe(
        map((body) => {
          const raw = (body as { permissions?: unknown } | null)?.permissions;
          return Array.isArray(raw)
            ? raw.filter((p): p is string => typeof p === 'string' && PERMISSION.test(p))
            : [];
        }),
      ),
    );
  }
}
