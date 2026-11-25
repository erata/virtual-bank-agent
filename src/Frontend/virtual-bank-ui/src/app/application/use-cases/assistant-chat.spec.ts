import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AssistantReply } from '../../domain/assistant';
import { Result, ok } from '../../domain/result';
import { AssistantGateway } from '../ports/assistant-gateway';
import { AssistantChat } from './assistant-chat';

class RecordingAssistantGateway extends AssistantGateway {
  messages: string[] = [];
  ask(message: string): Promise<Result<AssistantReply>> {
    this.messages.push(message);
    return Promise.resolve(
      ok({ answer: 'ok', mode: 'offline', partial: false, toolCalls: [], notices: [] }),
    );
  }
}

describe('assistant chat', () => {
  let gateway: RecordingAssistantGateway;

  beforeEach(() => {
    gateway = new RecordingAssistantGateway();
    TestBed.configureTestingModule({
      providers: [{ provide: AssistantGateway, useValue: gateway }],
    });
  });

  it('sends the trimmed message', async () => {
    const result = await TestBed.inject(AssistantChat).ask('  show K-100  ');
    expect(result.kind).toBe('ok');
    expect(gateway.messages).toEqual(['show K-100']);
  });

  it('rejects empty and oversized messages without a request', async () => {
    const chat = TestBed.inject(AssistantChat);
    expect((await chat.ask('   ')).kind).toBe('invalid');
    expect((await chat.ask('x'.repeat(2001))).kind).toBe('invalid');
    expect(gateway.messages).toEqual([]);
  });
});
