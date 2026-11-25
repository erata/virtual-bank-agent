import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { DocumentLibrary } from '../../../application/use-cases/document-library';
import {
  DocumentSummary,
  classificationLabel,
  formatLabel,
  matchesDocument,
} from '../../../domain/document';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';

/** Every document the API lets this user read (database RLS + ACL policy). Click a row to read it. */
@Component({
  selector: 'ba-documents-page',
  imports: [RouterLink, ProblemView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('docs.title') }}</h1>
    @if (documents.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (documents.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (documents.value(); as list) {
      <p class="muted">
        {{ i18n.t('docs.count', { n: list.length }) }}
        <a routerLink="/assistant">{{ i18n.t('docs.askAssistant') }}</a>.
      </p>
      <p>
        <input
          type="search"
          class="filter"
          [placeholder]="i18n.t('docs.filter')"
          [attr.aria-label]="i18n.t('docs.filter')"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
          data-testid="document-filter"
        />
      </p>
      <table data-testid="document-table">
        <thead>
          <tr>
            <th>{{ i18n.t('common.id') }}</th>
            <th>{{ i18n.t('docs.colTitle') }}</th>
            <th>{{ i18n.t('docs.format') }}</th>
            <th>{{ i18n.t('docs.language') }}</th>
            <th>{{ i18n.t('docs.classification') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (d of filtered(); track d.documentId) {
            <tr>
              <td>
                <a [routerLink]="['/documents', d.documentId]">{{ d.documentId }}</a>
              </td>
              <td>
                <a [routerLink]="['/documents', d.documentId]">{{ d.title }}</a>
                @if (d.fileName) {
                  <div class="muted small">
                    <code>{{ d.fileName }}</code>
                  </div>
                }
              </td>
              <td>
                <span class="badge">{{ formatName(d.format) }}</span>
              </td>
              <td>{{ d.language.toUpperCase() }}</td>
              <td>
                <span class="badge" [attr.data-classification]="d.classification">{{
                  i18n.t(classKey(d.classification))
                }}</span>
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="5" class="muted">{{ i18n.t('docs.none') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class DocumentsPage implements OnInit {
  protected readonly i18n = inject(I18n);

  private readonly library = inject(DocumentLibrary);

  protected readonly documents = new AsyncResult<readonly DocumentSummary[]>();
  protected readonly query = signal('');
  protected readonly filtered = computed(() =>
    (this.documents.value() ?? []).filter((d) => matchesDocument(d, this.query())),
  );
  protected readonly label = classificationLabel;

  protected classKey(level: number): MessageKey {
    return `docs.class.${Math.min(3, Math.max(0, level))}` as MessageKey;
  }
  protected readonly formatName = formatLabel;

  ngOnInit(): void {
    void this.documents.load(() => this.library.list());
  }
}
