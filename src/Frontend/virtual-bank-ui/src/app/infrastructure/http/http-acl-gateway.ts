import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { AclGateway } from '../../application/ports/acl-gateway';
import { AclEntry, AclIndexItem, AclRevision, AclSnapshot } from '../../domain/acl';
import { Result } from '../../domain/result';
import { call } from './call';

/** Adapter: /api/acl/documents and /api/documents/{id}/acl[...]. */
@Injectable()
export class HttpAclGateway extends AclGateway {
  private readonly http = inject(HttpClient);

  list(): Promise<Result<readonly AclIndexItem[]>> {
    return call(this.http.get<AclIndexItem[]>('/api/acl/documents'));
  }

  read(documentId: string): Promise<Result<AclSnapshot>> {
    return call(this.http.get<AclSnapshot>(`/api/documents/${encodeURIComponent(documentId)}/acl`));
  }

  update(
    documentId: string,
    expectedVersion: number,
    entries: readonly AclEntry[],
  ): Promise<Result<AclSnapshot>> {
    return call(
      this.http.put<AclSnapshot>(`/api/documents/${encodeURIComponent(documentId)}/acl`, {
        expectedVersion,
        entries,
      }),
    );
  }

  history(documentId: string): Promise<Result<readonly AclRevision[]>> {
    return call(
      this.http.get<AclRevision[]>(`/api/documents/${encodeURIComponent(documentId)}/acl/history`),
    );
  }
}
