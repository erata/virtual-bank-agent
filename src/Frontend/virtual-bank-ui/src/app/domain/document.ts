/** Classification levels as the API sends them (0..3); higher values need higher clearance. */
export type DocumentClassification = 0 | 1 | 2 | 3;

export interface DocumentSummary {
  readonly documentId: string;
  readonly title: string;
  readonly language: string;
  readonly classification: DocumentClassification;
  readonly fileName?: string | null;
  readonly format?: string | null;
  readonly updatedAt?: string | null;
}

/** One page of indexed text; pageNumber is null for non-paginated sources (TXT, MD, JSON, DOCX). */
export interface DocumentPage {
  readonly pageNumber: number | null;
  readonly text: string;
}

/**
 * One structural element for rendering. Text only: every value is shown by interpolation, so markup inside a
 * document is displayed as text and never becomes HTML.
 */
export interface DocumentBlock {
  readonly kind: 'title' | 'subtitle' | 'heading' | 'paragraph' | 'listItem' | 'table' | 'code';
  readonly level: number;
  readonly text: string | null;
  readonly rows?: readonly (readonly string[])[] | null;
}

export interface DocumentContent {
  readonly documentId: string;
  readonly title: string;
  readonly classification: DocumentClassification;
  readonly pages: readonly DocumentPage[];
  readonly fileName?: string | null;
  readonly format?: string | null;
  readonly language?: string | null;
  readonly updatedAt?: string | null;
  /** Server-side structure for DOCX; absent for other formats. */
  readonly blocks?: readonly DocumentBlock[] | null;
}

const DOCUMENT_ID = /^DOC-[0-9]{3}$/;

export function isDocumentId(value: string): boolean {
  return DOCUMENT_ID.test(value);
}

export function classificationLabel(level: number): string {
  switch (level) {
    case 0:
      return 'Public';
    case 1:
      return 'Internal';
    case 2:
      return 'Confidential';
    default:
      return 'Strictly confidential';
  }
}

export function formatLabel(format: string | null | undefined): string {
  switch (format) {
    case 'pdf':
      return 'PDF';
    case 'docx':
      return 'Word';
    case 'json':
      return 'JSON';
    case 'md':
      return 'Markdown';
    case 'txt':
      return 'Text';
    default:
      return format ? format.toUpperCase() : '—';
  }
}

/** Folds case and Turkish/German diacritics so "sozlesme" finds "Sözleşme". */
export function foldText(value: string): string {
  return value
    .replace(/[ıİI]/g, 'i')
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '');
}

/** Client-side list filter over ID, title and file name. */
export function matchesDocument(doc: DocumentSummary, query: string): boolean {
  const q = foldText(query.trim());
  return (
    q === '' ||
    foldText(doc.documentId).includes(q) ||
    foldText(doc.title).includes(q) ||
    foldText(doc.fileName ?? '').includes(q)
  );
}

/** Indented JSON, or null when the text is not valid JSON (the viewer then shows the raw text). */
export function prettyJson(text: string): string | null {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return null;
  }
}

export interface JsonToken {
  readonly kind: 'key' | 'string' | 'number' | 'literal' | 'punct';
  readonly text: string;
}

/** Splits pretty-printed JSON into tokens for colouring; concatenating all token texts gives the input back. */
export function tokenizeJson(pretty: string): readonly JsonToken[] {
  const pattern =
    /("(?:\\.|[^"\\])*")(\s*:)?|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|\b(true|false|null)\b/g;
  const tokens: JsonToken[] = [];
  let last = 0;
  for (const m of pretty.matchAll(pattern)) {
    if (m.index > last) tokens.push({ kind: 'punct', text: pretty.slice(last, m.index) });
    if (m[1] !== undefined) {
      tokens.push({ kind: m[2] ? 'key' : 'string', text: m[1] });
      if (m[2]) tokens.push({ kind: 'punct', text: m[2] });
    } else if (m[3] !== undefined) {
      tokens.push({ kind: 'number', text: m[3] });
    } else {
      tokens.push({ kind: 'literal', text: m[4] });
    }
    last = m.index + m[0].length;
  }
  if (last < pretty.length) tokens.push({ kind: 'punct', text: pretty.slice(last) });
  return tokens;
}

/**
 * Minimal Markdown to blocks: headings, bullet/numbered lists, pipe tables, fenced code and paragraphs.
 * Inline emphasis markers are removed; no HTML is ever produced.
 */
export function markdownBlocks(markdown: string): readonly DocumentBlock[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: DocumentBlock[] = [];
  let paragraph: string[] = [];
  const inline = (s: string) =>
    s
      .replace(/(\*\*|__)(.+?)\1/g, '$2')
      .replace(/`([^`]+)`/g, '$1')
      .trim();
  const flush = () => {
    if (paragraph.length)
      blocks.push({ kind: 'paragraph', level: 0, text: inline(paragraph.join(' ')) });
    paragraph = [];
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();
    if (trimmed.startsWith('```')) {
      flush();
      const code: string[] = [];
      while (++i < lines.length && !lines[i].trim().startsWith('```')) code.push(lines[i]);
      blocks.push({ kind: 'code', level: 0, text: code.join('\n') });
      continue;
    }
    const heading = /^(#{1,6})\s+(.*)$/.exec(trimmed);
    if (heading) {
      flush();
      blocks.push({
        kind: 'heading',
        level: Math.min(4, heading[1].length),
        text: inline(heading[2]),
      });
      continue;
    }
    const item = /^(?:[-*+]|\d+[.)])\s+(.*)$/.exec(trimmed);
    if (item) {
      flush();
      blocks.push({ kind: 'listItem', level: 0, text: inline(item[1]) });
      continue;
    }
    if (trimmed.startsWith('|')) {
      flush();
      const rows: string[][] = [];
      for (; i < lines.length && lines[i].trim().startsWith('|'); i++) {
        const cells = lines[i]
          .trim()
          .replace(/^\||\|$/g, '')
          .split('|')
          .map((c) => inline(c));
        if (!cells.every((c) => /^:?-{2,}:?$/.test(c))) rows.push(cells);
      }
      i--;
      blocks.push({ kind: 'table', level: 0, text: null, rows });
      continue;
    }
    if (trimmed === '') flush();
    else paragraph.push(trimmed);
  }
  flush();
  return blocks;
}
