import { AclEntry, AclIndexItem, AclRevision, AclSnapshot } from '../../domain/acl';
import { Result } from '../../domain/result';

/** Port to the document ACL administration endpoints (document.acl.manage). */
export abstract class AclGateway {
  abstract list(): Promise<Result<readonly AclIndexItem[]>>;

  abstract read(documentId: string): Promise<Result<AclSnapshot>>;

  /** Optimistic concurrency: fails if someone else changed the ACL since {@link expectedVersion}. */
  abstract update(
    documentId: string,
    expectedVersion: number,
    entries: readonly AclEntry[],
  ): Promise<Result<AclSnapshot>>;

  abstract history(documentId: string): Promise<Result<readonly AclRevision[]>>;
}
