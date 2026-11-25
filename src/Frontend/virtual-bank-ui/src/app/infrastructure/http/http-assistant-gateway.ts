import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { fromEvent, map, takeUntil } from 'rxjs';
import { AssistantGateway } from '../../application/ports/assistant-gateway';
import {
  AssistantCapabilities,
  AssistantDataTable,
  AssistantReply,
  ToolCall,
  ToolCallStatus,
} from '../../domain/assistant';
import { PendingAction, parsePendingAction } from '../../domain/actions';
import { Result } from '../../domain/result';
import { call } from './call';
import { isNoticeCode } from './problem-mapper';

const STATUSES: readonly ToolCallStatus[] = [
  'SUCCEEDED',
  'NOT_FOUND',
  'DENIED',
  'INVALID',
  'FAILED',
];

/**
 * Adapter: POST /api/assistant/chat. The response is re-shaped defensively: notice codes that do not look like
 * codes are dropped and unknown statuses become FAILED, so the UI never renders unexpected server values as notices.
 */
@Injectable()
export class HttpAssistantGateway extends AssistantGateway {
  private readonly http = inject(HttpClient);

  override capabilities(language = 'tr'): Promise<Result<AssistantCapabilities>> {
    const lang = ['tr', 'de', 'en'].includes(language) ? language : 'en';
    return call(
      this.http
        .get<unknown>(`/api/assistant/capabilities?lang=${lang}`)
        .pipe(map(sanitizeCapabilities)),
    );
  }

  ask(
    message: string,
    conversationId?: string | null,
    signal?: AbortSignal,
  ): Promise<Result<AssistantReply>> {
    let request = this.http
      .post<unknown>('/api/assistant/chat', { message, conversationId })
      .pipe(map(sanitizeReply));
    // Unsubscribing aborts the XHR; nginx then closes the upstream connection and the API sees RequestAborted.
    if (signal) request = request.pipe(takeUntil(fromEvent(signal, 'abort')));
    return call(request);
  }
}

export function sanitizeReply(body: unknown): AssistantReply {
  const raw = (typeof body === 'object' && body !== null ? body : {}) as Record<string, unknown>;
  const calls = Array.isArray(raw['toolCalls']) ? (raw['toolCalls'] as unknown[]) : [];
  return {
    answer: typeof raw['answer'] === 'string' ? raw['answer'] : null,
    mode: typeof raw['mode'] === 'string' ? raw['mode'] : 'unknown',
    partial: raw['partial'] === true,
    toolCalls: calls.map((c, index): ToolCall => {
      const item = (typeof c === 'object' && c !== null ? c : {}) as Record<string, unknown>;
      const status = STATUSES.find((s) => s === item['status']) ?? 'FAILED';
      return {
        sequence: typeof item['sequence'] === 'number' ? item['sequence'] : index + 1,
        tool:
          typeof item['tool'] === 'string' && /^[a-z_]{1,64}$/.test(item['tool'])
            ? item['tool']
            : 'unknown',
        status,
        noticeCode: isNoticeCode(item['noticeCode']) ? item['noticeCode'] : null,
      };
    }),
    notices: Array.isArray(raw['notices']) ? raw['notices'].filter(isNoticeCode) : [],
    ...(Array.isArray(raw['tables'])
      ? {
          tables: (raw['tables'] as unknown[])
            .slice(0, 12)
            .map(sanitizeTable)
            .filter((t): t is AssistantDataTable => t !== null),
        }
      : {}),
    ...(typeof raw['conversationId'] === 'string' ? { conversationId: raw['conversationId'] } : {}),
    ...(Array.isArray(raw['actions'])
      ? {
          actions: raw['actions']
            .slice(0, 5)
            .map(parsePendingAction)
            .filter((a): a is PendingAction => a !== null),
        }
      : {}),
    ...(Array.isArray(raw['citations'])
      ? {
          citations: raw['citations']
            .map((value) => {
              const row = (typeof value === 'object' && value !== null ? value : {}) as Record<
                string,
                unknown
              >;
              return {
                documentId:
                  typeof row['documentId'] === 'string' && /^DOC-[0-9]{3}$/.test(row['documentId'])
                    ? row['documentId']
                    : '',
                title: typeof row['title'] === 'string' ? row['title'].slice(0, 160) : '',
                section: typeof row['section'] === 'string' ? row['section'].slice(0, 100) : '',
                pageNumber:
                  typeof row['pageNumber'] === 'number' &&
                  Number.isSafeInteger(row['pageNumber']) &&
                  (row['pageNumber'] as number) > 0
                    ? (row['pageNumber'] as number)
                    : null,
              };
            })
            .filter((entry) => entry.documentId !== ''),
        }
      : {}),
  };
}

/** Defensive wire parsing. Cells remain plain text and Angular renders by interpolation (never innerHTML). */
function sanitizeTable(value: unknown): AssistantDataTable | null {
  if (typeof value !== 'object' || value === null) return null;
  const raw = value as Record<string, unknown>;
  const columns = raw['columns'];
  const rows = raw['rows'];
  if (
    !Array.isArray(columns) ||
    columns.length < 1 ||
    columns.length > 10 ||
    !columns.every((c) => typeof c === 'string' && c.length <= 100) ||
    !Array.isArray(rows)
  )
    return null;
  const columnCount = columns.length;
  const safeRows: string[][] = [];
  for (const row of rows.slice(0, 100)) {
    if (
      !Array.isArray(row) ||
      row.length !== columnCount ||
      !row.every((cell) => typeof cell === 'string' && cell.length <= 500)
    )
      return null;
    safeRows.push(row as string[]);
  }
  return {
    title: typeof raw['title'] === 'string' ? raw['title'].slice(0, 120) : 'Results',
    columns: columns as string[],
    rows: safeRows,
    totalCount:
      typeof raw['totalCount'] === 'number' &&
      Number.isSafeInteger(raw['totalCount']) &&
      raw['totalCount'] >= safeRows.length
        ? raw['totalCount']
        : safeRows.length,
  };
}

/** Read-only server capability data; never infer business permissions from the browser role. */
export function sanitizeCapabilities(body: unknown): AssistantCapabilities {
  const data = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
  const tools = Array.isArray(data['availableTools']) ? data['availableTools'] : [];
  const suggestions = Array.isArray(data['suggestions']) ? data['suggestions'] : [];
  return {
    mode: typeof data['mode'] === 'string' ? data['mode'].slice(0, 20) : 'unknown',
    introduction:
      typeof data['introduction'] === 'string' ? data['introduction'].slice(0, 500) : '',
    availableTools: tools
      .filter((t): t is string => typeof t === 'string' && /^[a-z_]{1,64}$/.test(t))
      .slice(0, 40),
    suggestions: suggestions
      .slice(0, 12)
      .flatMap((raw): { label: string; prompt: string; tool: string }[] => {
        if (!raw || typeof raw !== 'object') return [];
        const row = raw as Record<string, unknown>;
        if (
          typeof row['label'] !== 'string' ||
          typeof row['prompt'] !== 'string' ||
          typeof row['tool'] !== 'string' ||
          row['label'].length > 100 ||
          row['prompt'].length > 500 ||
          !/^[a-z_]{1,64}$/.test(row['tool'])
        )
          return [];
        return [{ label: row['label'], prompt: row['prompt'], tool: row['tool'] }];
      }),
  };
}
