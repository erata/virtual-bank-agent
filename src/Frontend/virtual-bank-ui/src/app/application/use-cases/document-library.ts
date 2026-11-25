import { Injectable, inject } from '@angular/core';
import { DocumentContent, DocumentSummary, isDocumentId } from '../../domain/document';
import { Result, notFound } from '../../domain/result';
import { DocumentGateway } from '../ports/document-gateway';

/** Document list and viewer. A malformed ID is answered locally with "not found", like the API would. */
@Injectable({ providedIn: 'root' })
export class DocumentLibrary {
  private readonly gateway = inject(DocumentGateway);

  list(): Promise<Result<readonly DocumentSummary[]>> {
    return this.gateway.list();
  }

  async open(documentId: string): Promise<Result<DocumentContent>> {
    return isDocumentId(documentId) ? this.gateway.get(documentId) : notFound();
  }

  async original(documentId: string): Promise<Result<Blob>> {
    return isDocumentId(documentId) ? this.gateway.original(documentId) : notFound();
  }
}
