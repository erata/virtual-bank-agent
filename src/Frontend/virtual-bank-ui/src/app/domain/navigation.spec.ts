import { describe, expect, it } from 'vitest';
import { visibleSections } from './access';
import { DocumentSummary, isDocumentId, matchesDocument } from './document';

const labels = (permissions: string[]) => visibleSections(permissions).map((s) => s.label);

describe('menu follows the effective permissions from the API', () => {
  it('bank customer sees own banking, documents and assistant, not staff pages', () => {
    expect(labels(['assistant.use', 'customer.read.self', 'document.read'])).toEqual([
      'My banking',
      'Documents',
      'Assistant',
      'My access',
    ]);
  });

  it('advisor sees customers but not credit or self-service', () => {
    expect(labels(['assistant.use', 'customer.read', 'document.read'])).toEqual([
      'Customers',
      'Documents',
      'Assistant',
      'My access',
    ]);
  });

  it('credit analyst with assigned-customer access sees customers and credit', () => {
    expect(labels(['credit.read', 'customer.read.assigned'])).toEqual([
      'Customers',
      'Credit applications',
      'My access',
    ]);
  });

  it('nothing loaded yet (or failed) shows only the always-visible page', () => {
    expect(labels([])).toEqual(['My access']);
  });
});

describe('documents', () => {
  const doc: DocumentSummary = {
    documentId: 'DOC-118',
    title: 'SEPA Havale ve Ödeme Rehberi 2026',
    language: 'tr',
    classification: 0,
  };

  it.each([
    ['', true],
    ['doc-118', true],
    ['odeme', true], // diacritic-free keyboard input
    ['ÖDEME', true],
    ['havale rehberi', false],
    ['kredi', false],
  ])('filter %s matches=%s', (query, expected) => {
    expect(matchesDocument(doc, query)).toBe(expected);
  });

  it.each([
    ['DOC-101', true],
    ['DOC-1010', false],
    ['../acl', false],
  ])('document id %s valid=%s', (id, valid) => {
    expect(isDocumentId(id)).toBe(valid);
  });
});

describe('document rendering helpers', () => {
  it('pretty-prints JSON and keeps invalid JSON as raw text', async () => {
    const { prettyJson } = await import('./document');
    expect(prettyJson('{"a":[1,true]}')).toBe('{\n  "a": [\n    1,\n    true\n  ]\n}');
    expect(prettyJson('{not json')).toBeNull();
  });

  it('JSON tokens reassemble exactly and mark keys separately from values', async () => {
    const { tokenizeJson } = await import('./document');
    const pretty = '{\n  "title": "Gebühren",\n  "count": 3,\n  "ok": null\n}';
    const tokens = tokenizeJson(pretty);
    expect(tokens.map((t) => t.text).join('')).toBe(pretty);
    expect(tokens.find((t) => t.text === '"title"')?.kind).toBe('key');
    expect(tokens.find((t) => t.text === '"Gebühren"')?.kind).toBe('string');
    expect(tokens.find((t) => t.text === '3')?.kind).toBe('number');
  });

  it('markdown becomes headings, list items, tables and paragraphs, never HTML', async () => {
    const { markdownBlocks } = await import('./document');
    const blocks = markdownBlocks(
      '# Runbook\n\nIntro **bold** text\ncontinues.\n\n- first\n- <script>x</script>\n\n| A | B |\n|---|---|\n| 1 | 2 |\n',
    );
    expect(blocks.map((b) => b.kind)).toEqual([
      'heading',
      'paragraph',
      'listItem',
      'listItem',
      'table',
    ]);
    expect(blocks[1].text).toBe('Intro bold text continues.');
    expect(blocks[3].text).toBe('<script>x</script>'); // shown as text by interpolation
    expect(blocks[4].rows).toEqual([
      ['A', 'B'],
      ['1', '2'],
    ]);
  });
});

describe('UI language', () => {
  it('explicit choice, then browser language, then English', async () => {
    const { pickLanguage } = await import('./language');
    expect(pickLanguage('de', ['tr-TR'])).toBe('de');
    expect(pickLanguage(null, ['fr-FR', 'tr-TR'])).toBe('tr');
    expect(pickLanguage(null, ['de-AT'])).toBe('de');
    expect(pickLanguage('xx', ['fr-FR'])).toBe('en');
  });
});

describe('ACL editor rules (mirror the API)', () => {
  it('rejects malformed groups, non-* public rules and duplicates', async () => {
    const { aclProblems } = await import('./acl');
    expect(
      aclProblems([{ subjectKind: 'group', subjectValue: '/Credit/Approval', effect: 'allow' }]),
    ).toEqual([]);
    expect(
      aclProblems([{ subjectKind: 'group', subjectValue: 'Credit', effect: 'allow' }]),
    ).toContain('group');
    expect(
      aclProblems([{ subjectKind: 'public', subjectValue: 'everyone', effect: 'allow' }]),
    ).toContain('public');
    const rule = { subjectKind: 'role', subjectValue: 'auditor', effect: 'deny' } as const;
    expect(aclProblems([rule, rule])).toContain('duplicate');
    expect(aclProblems([])).toContain('count');
  });
});
