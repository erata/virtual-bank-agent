import { describe, expect, it } from 'vitest';
import { GENERIC_FORBIDDEN, noticeText } from './shared/notice-messages';

describe('notice texts', () => {
  it('explains known notice codes', () => {
    expect(noticeText('CREDIT_FOUR_EYES_REQUIRED')).toContain('four-eyes');
  });

  it('falls back to a generic text for unknown or missing codes', () => {
    expect(noticeText('SOMETHING_NEW')).toBe(GENERIC_FORBIDDEN);
    expect(noticeText(null)).toBe(GENERIC_FORBIDDEN);
    expect(noticeText('CREDIT_FOUR_EYES_REQUIRED', 'de')).toContain('Vier-Augen');
    expect(noticeText('CREDIT_FOUR_EYES_REQUIRED', 'tr')).toContain('dört göz');
  });
});
