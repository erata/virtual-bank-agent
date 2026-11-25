import { AssistantCapabilities, AssistantReply } from '../../domain/assistant';
import { Result, invalid } from '../../domain/result';

/** Port to the assistant endpoint. The caller's identity comes from the access token, never from the message. */
export abstract class AssistantGateway {
  /** Aborting {@link signal} cancels the HTTP request; the API then stops the model call and stores nothing. */
  abstract ask(
    message: string,
    conversationId?: string | null,
    signal?: AbortSignal,
  ): Promise<Result<AssistantReply>>;
  // Existing test adapters may omit the optional capability discovery operation.
  capabilities(_language = 'tr'): Promise<Result<AssistantCapabilities>> {
    return Promise.resolve(invalid('capabilities', 'Not supported by this adapter.'));
  }
}
