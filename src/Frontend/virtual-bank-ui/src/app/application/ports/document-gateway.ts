import { DocumentContent, DocumentSummary } from '../../domain/document';
import { Result } from '../../domain/result';

/** Port to the document endpoints. The API applies database RLS and the ACL policy to every call. */
export abstract class DocumentGateway {
  abstract list(): Promise<Result<readonly DocumentSummary[]>>;

  abstract get(documentId: string): Promise<Result<DocumentContent>>;

  /** Original file bytes (PDF, DOCX, JSON, MD, TXT), authorized like the text view. */
  abstract original(documentId: string): Promise<Result<Blob>>;
}
