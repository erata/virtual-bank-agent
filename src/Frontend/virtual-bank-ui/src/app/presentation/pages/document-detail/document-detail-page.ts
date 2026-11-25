import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { DocumentLibrary } from '../../../application/use-cases/document-library';
import {
  DocumentBlock,
  DocumentContent,
  JsonToken,
  classificationLabel,
  formatLabel,
  markdownBlocks,
  prettyJson,
  tokenizeJson,
} from '../../../domain/document';
import { Problem } from '../../../domain/result';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';

/** Renders structural blocks by interpolation only: document text can never become HTML. */
@Component({
  selector: 'ba-document-blocks',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (b of blocks(); track $index) {
      @switch (b.kind) {
        @case ('title') {
          <h2 class="doc-title">{{ b.text }}</h2>
        }
        @case ('subtitle') {
          <p class="doc-subtitle">{{ b.text }}</p>
        }
        @case ('heading') {
          @if (b.level <= 1) {
            <h3>{{ b.text }}</h3>
          } @else if (b.level === 2) {
            <h4>{{ b.text }}</h4>
          } @else {
            <h5>{{ b.text }}</h5>
          }
        }
        @case ('listItem') {
          <p class="doc-list-item">{{ b.text }}</p>
        }
        @case ('code') {
          <pre class="code">{{ b.text }}</pre>
        }
        @case ('table') {
          <table class="doc-table">
            <tbody>
              @for (row of b.rows ?? []; track $index; let header = $first) {
                <tr>
                  @for (cell of row; track $index) {
                    @if (header) {
                      <th scope="col">{{ cell }}</th>
                    } @else {
                      <td>{{ cell }}</td>
                    }
                  }
                </tr>
              }
            </tbody>
          </table>
        }
        @default {
          <p>{{ b.text }}</p>
        }
      }
    }
  `,
})
export class DocumentBlocks {
  readonly blocks = input.required<readonly DocumentBlock[]>();
}

type View = 'formatted' | 'text';

/**
 * One document as its format deserves: PDF in the browser's PDF viewer, Word as headings and paragraphs, JSON
 * indented and coloured, Markdown rendered, plain text as is. A document the user may not read is "not found",
 * exactly like one that does not exist.
 */
@Component({
  selector: 'ba-document-detail-page',
  imports: [RouterLink, ProblemView, DatePipe, DocumentBlocks],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>
      <a routerLink="/documents">{{ i18n.t('docs.back') }}</a>
    </p>
    @if (doc.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (doc.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (doc.value(); as d) {
      <h1 data-testid="document-title">{{ d.title }}</h1>
      <dl class="doc-meta" data-testid="document-meta">
        <div>
          <dt>{{ i18n.t('common.id') }}</dt>
          <dd>{{ d.documentId }}</dd>
        </div>
        <div>
          <dt>{{ i18n.t('docs.file') }}</dt>
          <dd>
            <code>{{ d.fileName ?? '—' }}</code>
          </dd>
        </div>
        <div>
          <dt>{{ i18n.t('docs.format') }}</dt>
          <dd>
            <span class="badge">{{ formatName(d.format) }}</span>
          </dd>
        </div>
        <div>
          <dt>{{ i18n.t('docs.classification') }}</dt>
          <dd>
            <span class="badge" [attr.data-classification]="d.classification">{{
              i18n.t(classKey(d.classification))
            }}</span>
          </dd>
        </div>
        <div>
          <dt>{{ i18n.t('docs.language') }}</dt>
          <dd>{{ (d.language ?? '—').toUpperCase() }}</dd>
        </div>
        @if (d.updatedAt) {
          <div>
            <dt>{{ i18n.t('docs.indexed') }}</dt>
            <dd>{{ d.updatedAt | date: 'yyyy-MM-dd HH:mm' }}</dd>
          </div>
        }
      </dl>

      <div class="doc-actions">
        <div class="segmented" role="group" aria-label="View">
          <button
            type="button"
            [class.active]="view() === 'formatted'"
            (click)="view.set('formatted')"
            data-testid="view-formatted"
          >
            {{ i18n.t('docs.formatted') }}
          </button>
          <button
            type="button"
            [class.active]="view() === 'text'"
            (click)="view.set('text')"
            data-testid="view-text"
          >
            {{ i18n.t('docs.text') }}
          </button>
        </div>
        <button
          type="button"
          class="secondary"
          (click)="download()"
          [disabled]="!original()"
          data-testid="document-download"
        >
          {{ i18n.t('docs.download') }}
        </button>
        <a
          class="button secondary"
          [routerLink]="['/assistant']"
          [queryParams]="{ q: i18n.t('docs.askPrompt', { id: d.documentId }) }"
        >
          {{ i18n.t('docs.ask') }}
        </a>
      </div>

      @if (view() === 'text') {
        <article class="document-text" data-testid="document-text">
          @for (page of d.pages; track $index) {
            <section>
              @if (page.pageNumber !== null && d.pages.length > 1) {
                <h2 class="page-heading">{{ i18n.t('docs.page', { n: page.pageNumber }) }}</h2>
              }
              <p>{{ page.text }}</p>
            </section>
          } @empty {
            <p class="muted">{{ i18n.t('docs.noText') }}</p>
          }
        </article>
        <p class="muted">{{ i18n.t('docs.textNote') }}</p>
      } @else {
        @if (originalProblem(); as problem) {
          <ba-problem [problem]="problem" />
        } @else if (!original()) {
          <p class="muted">{{ i18n.t('docs.loadingOriginal') }}</p>
        } @else {
          @switch (d.format) {
            @case ('pdf') {
              <iframe
                class="pdf-frame"
                [src]="pdfUrl()"
                title="PDF: {{ d.title }}"
                data-testid="document-pdf"
              ></iframe>
            }
            @case ('docx') {
              <article class="document-text rendered" data-testid="document-rendered">
                <ba-document-blocks [blocks]="d.blocks ?? []" />
              </article>
            }
            @case ('md') {
              <article class="document-text rendered" data-testid="document-rendered">
                <ba-document-blocks [blocks]="markdown()" />
              </article>
            }
            @case ('json') {
              <pre
                class="code json"
                data-testid="document-json"
              >@for (t of json(); track $index) {<span [class]="'json-' + t.kind">{{ t.text }}</span>}</pre>
            }
            @default {
              <pre class="code plain" data-testid="document-plain">{{ originalText() }}</pre>
            }
          }
        }
      }
    }
  `,
})
export class DocumentDetailPage {
  protected readonly i18n = inject(I18n);

  private readonly library = inject(DocumentLibrary);
  private readonly sanitizer = inject(DomSanitizer);

  /** Bound from the route parameter (withComponentInputBinding). */
  readonly documentId = input.required<string>();

  protected readonly doc = new AsyncResult<DocumentContent>();
  protected readonly view = signal<View>('formatted');
  protected readonly original = signal<Blob | null>(null);
  protected readonly originalProblem = signal<Problem | null>(null);
  protected readonly originalText = signal('');
  private readonly pdfObjectUrl = signal<string | null>(null);

  protected readonly label = classificationLabel;

  protected classKey(level: number): MessageKey {
    return `docs.class.${Math.min(3, Math.max(0, level))}` as MessageKey;
  }
  protected readonly formatName = formatLabel;

  protected readonly pdfUrl = computed<SafeResourceUrl | null>(() => {
    const url = this.pdfObjectUrl();
    // Only a blob: URL we created ourselves from an application/pdf Blob is ever trusted here.
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });
  protected readonly markdown = computed(() => markdownBlocks(this.originalText()));
  protected readonly json = computed<readonly JsonToken[]>(() => {
    const text = this.originalText();
    const pretty = prettyJson(text);
    return pretty === null ? [{ kind: 'punct', text }] : tokenizeJson(pretty);
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.releasePdf());
    // Only the route parameter is a dependency; load() reads and writes other signals, which must not
    // re-trigger this effect (that would reload the document in a loop).
    effect(() => {
      const id = this.documentId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    this.releasePdf();
    this.original.set(null);
    this.originalProblem.set(null);
    this.originalText.set('');
    this.view.set('formatted');
    await this.doc.load(() => this.library.open(id));
    const content = this.doc.value();
    if (!content) return;
    const result = await this.library.original(id);
    if (result.kind !== 'ok') {
      this.originalProblem.set(result);
      return;
    }
    if (content.format === 'pdf') {
      // Re-type the bytes ourselves: whatever the response said, this frame can only ever show a PDF.
      const pdf = new Blob([result.value], { type: 'application/pdf' });
      this.pdfObjectUrl.set(URL.createObjectURL(pdf));
    } else if (content.format !== 'docx') {
      this.originalText.set(await result.value.text());
    }
    this.original.set(result.value);
  }

  protected download(): void {
    const blob = this.original();
    const name = this.doc.value()?.fileName ?? 'document';
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  private releasePdf(): void {
    const url = this.pdfObjectUrl();
    if (url) URL.revokeObjectURL(url);
    this.pdfObjectUrl.set(null);
  }
}
