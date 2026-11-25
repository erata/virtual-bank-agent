import { describe, expect, it } from 'vitest';
import {
  CREDIT_NOTE_MAX_LENGTH,
  isCreditApplicationId,
  normalizeNoteText,
} from './credit-application';
import { isCustomerId } from './customer';

describe('domain rules (same as the backend)', () => {
  it.each([
    ['K-100', true],
    ['K-1000', false],
    ['k-100', false],
    ["K-100'--", false],
  ])('credit application id %s valid=%s', (id, valid) => {
    expect(isCreditApplicationId(id)).toBe(valid);
  });

  it.each([
    ['C-1002', true],
    ['C-12', false],
    ['../C-1002', false],
  ])('customer id %s valid=%s', (id, valid) => {
    expect(isCustomerId(id)).toBe(valid);
  });

  it('trims note text and enforces 1..4000 characters', () => {
    expect(normalizeNoteText('  ok  ')).toBe('ok');
    expect(normalizeNoteText('   ')).toBeNull();
    expect(normalizeNoteText('x'.repeat(CREDIT_NOTE_MAX_LENGTH))).not.toBeNull();
    expect(normalizeNoteText('x'.repeat(CREDIT_NOTE_MAX_LENGTH + 1))).toBeNull();
  });
});
