import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { AclAdministration } from '../../../application/use-cases/acl-administration';
import { AclIndexItem } from '../../../domain/acl';
import { foldText } from '../../../domain/document';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';

/** Every document's ACL version and entry count. The ACL admin sees metadata, never document content. */
@Component({
  selector: 'ba-acl-list-page',
  imports: [RouterLink, ProblemView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('acl.title') }}</h1>
    <p class="muted">{{ i18n.t('acl.lead') }}</p>
    @if (items.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (items.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else {
      <p>
        <input
          type="search"
          class="filter"
          [placeholder]="i18n.t('docs.filter')"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
          data-testid="acl-filter"
        />
      </p>
      <table data-testid="acl-table">
        <thead>
          <tr>
            <th>{{ i18n.t('common.id') }}</th>
            <th>{{ i18n.t('docs.colTitle') }}</th>
            <th>{{ i18n.t('docs.classification') }}</th>
            <th>{{ i18n.t('common.branch') }}</th>
            <th class="number">{{ i18n.t('acl.entries') }}</th>
            <th class="number">{{ i18n.t('common.version') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (d of filtered(); track d.documentId) {
            <tr>
              <td>
                <a [routerLink]="['/admin/acl', d.documentId]">{{ d.documentId }}</a>
              </td>
              <td>{{ d.title }}</td>
              <td>
                <span class="badge" [attr.data-classification]="d.classification">{{
                  i18n.t(classKey(d.classification))
                }}</span>
              </td>
              <td>{{ d.branch ?? '—' }}</td>
              <td class="number">{{ d.entryCount }}</td>
              <td class="number">{{ d.version }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="muted">{{ i18n.t('docs.none') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class AclListPage implements OnInit {
  private readonly acl = inject(AclAdministration);
  protected readonly i18n = inject(I18n);

  protected readonly items = new AsyncResult<readonly AclIndexItem[]>();
  protected readonly query = signal('');
  protected readonly filtered = computed(() => {
    const q = foldText(this.query().trim());
    return (this.items.value() ?? []).filter(
      (d) => !q || foldText(d.documentId).includes(q) || foldText(d.title).includes(q),
    );
  });

  protected classKey(level: number): MessageKey {
    return `docs.class.${Math.min(3, Math.max(0, level))}` as MessageKey;
  }

  ngOnInit(): void {
    void this.items.load(() => this.acl.list());
  }
}
