import { PendingAction } from './actions';
/** One assistant turn as returned by POST /api/assistant/chat. */
export type ToolCallStatus = 'SUCCEEDED' | 'NOT_FOUND' | 'DENIED' | 'INVALID' | 'FAILED';

export interface ToolCall {
  readonly sequence: number;
  readonly tool: string;
  readonly status: ToolCallStatus;
  readonly noticeCode: string | null;
}

/**
 * `answer` is model text (narrative, untrusted). `toolCalls`, `notices` and `partial` are computed by the API's
 * application code and are what the UI relies on to tell the user what was not available.
 */
export interface AssistantCitation {
  readonly documentId: string;
  readonly title: string;
  readonly section: string;
  readonly pageNumber: number | null;
}

/** Structured, whitelisted rows produced by authorized C# use cases, never parsed from LLM prose. */
export interface AssistantDataTable {
  readonly title: string;
  readonly columns: readonly string[];
  readonly rows: readonly (readonly string[])[];
  readonly totalCount: number;
}

export interface AssistantReply {
  readonly answer: string | null;
  readonly mode: string;
  readonly partial: boolean;
  readonly toolCalls: readonly ToolCall[];
  readonly tables?: readonly AssistantDataTable[];
  readonly notices: readonly string[];
  readonly citations?: readonly AssistantCitation[];
  readonly conversationId?: string | null;
  /** Confirmation cards the assistant PREPARED; nothing happens unless the user confirms one. */
  readonly actions?: readonly PendingAction[];
}

export const ASSISTANT_MESSAGE_MAX_LENGTH = 2000;

export function normalizeAssistantMessage(raw: string): string | null {
  const text = raw.trim();
  return text.length === 0 || text.length > ASSISTANT_MESSAGE_MAX_LENGTH ? null : text;
}

export interface AssistantSuggestion {
  readonly label: string;
  readonly prompt: string;
  readonly tool: string;
}
export interface AssistantCapabilities {
  readonly mode: string;
  readonly introduction: string;
  readonly availableTools: readonly string[];
  readonly suggestions: readonly AssistantSuggestion[];
}
