import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CustomerDirectory } from '../../../application/use-cases/customer-directory';
import { CustomerSummary } from '../../../domain/customer';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';
import { I18n } from '../../i18n/i18n';

@Component({
  selector: 'ba-customers-page',
  imports: [RouterLink, ProblemView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('customers.title') }}</h1>
    @if (customers.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (customers.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (customers.value(); as list) {
      <p class="muted">{{ i18n.t('customers.count', { n: list.length }) }}</p>
      <table data-testid="customer-table">
        <thead>
          <tr>
            <th>{{ i18n.t('common.id') }}</th>
            <th>{{ i18n.t('common.name') }}</th>
            <th>{{ i18n.t('common.branch') }}</th>
            <th>{{ i18n.t('customers.segment') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (c of list; track c.id) {
            <tr>
              <td>
                <a [routerLink]="['/customers', c.id]">{{ c.id }}</a>
              </td>
              <td>{{ c.firstName }} {{ c.lastName }}</td>
              <td>{{ c.branch }}</td>
              <td>{{ c.segment }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="4" class="muted">{{ i18n.t('customers.none') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class CustomersPage implements OnInit {
  protected readonly i18n = inject(I18n);

  private readonly directory = inject(CustomerDirectory);

  protected readonly customers = new AsyncResult<readonly CustomerSummary[]>();

  ngOnInit(): void {
    void this.customers.load(() => this.directory.listCustomers());
  }
}
