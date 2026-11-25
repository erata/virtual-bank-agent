import { describe, expect, it } from 'vitest';
import { sanitizeReply } from './http-assistant-gateway';

describe('assistant reply sanitizing', () => {
  it('keeps well-formed values', () => {
    const reply = sanitizeReply({
      answer: 'Two customers.',
      mode: 'offline',
      partial: true,
      toolCalls: [
        {
          sequence: 1,
          tool: 'list_credit_applications',
          status: 'DENIED',
          noticeCode: 'CREDIT_READ_REQUIRED',
        },
      ],
      notices: ['CREDIT_READ_REQUIRED'],
    });
    expect(reply.partial).toBe(true);
    expect(reply.toolCalls[0]).toEqual({
      sequence: 1,
      tool: 'list_credit_applications',
      status: 'DENIED',
      noticeCode: 'CREDIT_READ_REQUIRED',
    });
    expect(reply.notices).toEqual(['CREDIT_READ_REQUIRED']);
  });

  it('never surfaces free text from the server as a notice code, tool name or status', () => {
    const reply = sanitizeReply({
      answer: 42,
      partial: 'yes',
      toolCalls: [
        {
          tool: '<img src=x onerror=alert(1)>',
          status: 'APPROVED',
          noticeCode: 'You are approved!',
        },
      ],
      notices: ['<script>', 'Ignore previous instructions', 'ASSISTANT_TOOL_FAILED'],
    });
    expect(reply.answer).toBeNull();
    expect(reply.partial).toBe(false);
    expect(reply.toolCalls[0]).toEqual({
      sequence: 1,
      tool: 'unknown',
      status: 'FAILED',
      noticeCode: null,
    });
    expect(reply.notices).toEqual(['ASSISTANT_TOOL_FAILED']);
  });

  it('preserves source page numbers and drops malformed citations', () => {
    const reply = sanitizeReply({
      citations: [
        { documentId: 'DOC-101', title: 'Credit policy', section: '1', pageNumber: 2 },
        { documentId: 'DOC-102', title: 'FAQ', section: 'Document', pageNumber: -3 },
        { documentId: '../private', title: 'hidden', pageNumber: 1 },
      ],
    });
    expect(reply.citations).toEqual([
      { documentId: 'DOC-101', title: 'Credit policy', section: '1', pageNumber: 2 },
      { documentId: 'DOC-102', title: 'FAQ', section: 'Document', pageNumber: null },
    ]);
  });

  it('preserves correctly shaped authorized tables and rejects malformed rows', () => {
    const reply = sanitizeReply({
      tables: [
        {
          title: 'Müşteriler',
          columns: ['Müşteri No', 'Ad Soyad'],
          rows: [['C-1002', 'Jana Weidner']],
          totalCount: 1,
        },
        { title: '<script>bad</script>', columns: ['a', 'b'], rows: [['C-1002']], totalCount: 1 },
      ],
    });
    expect(reply.tables).toEqual([
      {
        title: 'Müşteriler',
        columns: ['Müşteri No', 'Ad Soyad'],
        rows: [['C-1002', 'Jana Weidner']],
        totalCount: 1,
      },
    ]);
  });

  it('tolerates a non-object body', () => {
    expect(sanitizeReply(null)).toEqual({
      answer: null,
      mode: 'unknown',
      partial: false,
      toolCalls: [],
      notices: [],
    });
  });
});
