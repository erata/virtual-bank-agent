import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { DocumentGateway } from '../../application/ports/document-gateway';
import { DocumentContent, DocumentSummary } from '../../domain/document';
import { Result } from '../../domain/result';
import { call } from './call';

/** Adapter: GET /api/documents and GET /api/documents/{id}. Text is rendered by interpolation only. */
@Injectable()
export class HttpDocumentGateway extends DocumentGateway {
  private readonly http = inject(HttpClient);

  list(): Promise<Result<readonly DocumentSummary[]>> {
    return call(this.http.get<DocumentSummary[]>('/api/documents'));
  }

  get(documentId: string): Promise<Result<DocumentContent>> {
    return call(this.http.get<DocumentContent>(`/api/documents/${encodeURIComponent(documentId)}`));
  }

  original(documentId: string): Promise<Result<Blob>> {
    return call(
      this.http.get(`/api/documents/${encodeURIComponent(documentId)}/file`, {
        responseType: 'blob',
      }),
    );
  }
}
