import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n } from '../../i18n/i18n';

@Component({
  selector: 'ba-not-found-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1>{{ i18n.t('notFound.title') }}</h1>
    <p>
      <a routerLink="/">{{ i18n.t('notFound.back') }}</a>
    </p>`,
})
export class NotFoundPage {
  protected readonly i18n = inject(I18n);
}
