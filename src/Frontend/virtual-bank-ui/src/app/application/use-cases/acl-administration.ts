import { Injectable, inject } from '@angular/core';
import { AclEntry, AclIndexItem, AclRevision, AclSnapshot, aclProblems } from '../../domain/acl';
import { isDocumentId } from '../../domain/document';
import { Result, invalid, notFound } from '../../domain/result';
import { AclGateway } from '../ports/acl-gateway';

/** Document ACL administration. Entries are normalized and pre-validated; the API is still the authority. */
@Injectable({ providedIn: 'root' })
export class AclAdministration {
  private readonly gateway = inject(AclGateway);

  list(): Promise<Result<readonly AclIndexItem[]>> {
    return this.gateway.list();
  }

  async read(documentId: string): Promise<Result<AclSnapshot>> {
    return isDocumentId(documentId) ? this.gateway.read(documentId) : notFound();
  }

  async history(documentId: string): Promise<Result<readonly AclRevision[]>> {
    return isDocumentId(documentId) ? this.gateway.history(documentId) : notFound();
  }

  async save(
    documentId: string,
    expectedVersion: number,
    entries: readonly AclEntry[],
  ): Promise<Result<AclSnapshot>> {
    if (!isDocumentId(documentId)) return notFound();
    const normalized = entries.map((e) => ({ ...e, subjectValue: e.subjectValue.trim() }));
    const problems = aclProblems(normalized);
    return problems.length > 0
      ? invalid('entries', problems.join(','))
      : this.gateway.update(documentId, expectedVersion, normalized);
  }
}
