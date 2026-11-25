import { Injectable, inject } from '@angular/core';
import {
  ASSISTANT_MESSAGE_MAX_LENGTH,
  AssistantReply,
  AssistantCapabilities,
  normalizeAssistantMessage,
} from '../../domain/assistant';
import { Result, invalid } from '../../domain/result';
import { AssistantGateway } from '../ports/assistant-gateway';

/** One assistant turn. Validation mirrors the backend limit so users get feedback before a round trip. */
@Injectable({ providedIn: 'root' })
export class AssistantChat {
  private readonly gateway = inject(AssistantGateway);

  async capabilities(language = 'tr'): Promise<Result<AssistantCapabilities>> {
    return this.gateway.capabilities(language);
  }

  async ask(
    rawMessage: string,
    conversationId?: string | null,
    signal?: AbortSignal,
  ): Promise<Result<AssistantReply>> {
    const message = normalizeAssistantMessage(rawMessage);
    return message === null
      ? invalid(
          'message',
          `A message must contain 1 to ${ASSISTANT_MESSAGE_MAX_LENGTH} characters.`,
        )
      : this.gateway.ask(message, conversationId, signal);
  }
}
