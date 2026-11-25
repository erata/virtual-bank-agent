import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthSession } from '../../../application/ports/auth-session';
import { AccessNavigation } from '../../../application/use-cases/access-navigation';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';

type Texts = readonly [en: string, de: string, tr: string];

interface DemoPersona {
  readonly username: string;
  readonly role: Texts;
  readonly expectation: Texts;
}

const CUSTOMER_EXPECTATION: Texts = [
  'Own accounts only: transfers (SEPA and between own accounts), standing orders, credit applications; public documents and the assistant.',
  'Nur eigene Konten: Überweisungen (SEPA und Umbuchungen), Daueraufträge, Kreditanträge; öffentliche Dokumente und Assistent.',
  'Yalnızca kendi hesapları: havale (SEPA ve hesaplar arası), otomatik ödeme talimatı, kredi başvurusu; herkese açık belgeler ve asistan.',
];

/** Landing page. The persona table exists for the local demo realm only (development credentials). */
@Component({
  selector: 'ba-home-page',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('home.title') }}</h1>
    <p class="lead">{{ i18n.t('home.lead') }}</p>

    @if (signedIn()) {
      <p class="shortcuts" data-testid="home-shortcuts">
        @for (section of navigation.sections(); track section.path; let first = $first) {
          <a [routerLink]="section.path" class="button" [class.secondary]="!first">{{
            navLabel(section.key)
          }}</a>
        }
      </p>
    } @else {
      <p>
        <button type="button" (click)="auth.login()" data-testid="home-login">
          {{ i18n.t('home.signInKeycloak') }}
        </button>
      </p>
    }

    <h2>{{ i18n.t('home.personas') }}</h2>
    <p class="muted">{{ i18n.t('home.password', { pw: 'VirtualBank-Dev-2026!' }) }}</p>
    <table>
      <thead>
        <tr>
          <th>{{ i18n.t('home.user') }}</th>
          <th>{{ i18n.t('home.role') }}</th>
          <th>{{ i18n.t('home.expect') }}</th>
        </tr>
      </thead>
      <tbody>
        @for (p of personas; track p.username) {
          <tr>
            <td>
              <code>{{ p.username }}</code>
            </td>
            <td>{{ pick(p.role) }}</td>
            <td>{{ pick(p.expectation) }}</td>
          </tr>
        }
      </tbody>
    </table>
  `,
})
export class HomePage {
  protected readonly auth = inject(AuthSession);
  protected readonly navigation = inject(AccessNavigation);
  protected readonly i18n = inject(I18n);

  protected readonly signedIn = computed(() => this.auth.state().status === 'authenticated');

  protected navLabel(key: string): string {
    return this.i18n.t(`nav.${key}` as MessageKey);
  }

  protected pick(texts: Texts): string {
    return texts[{ en: 0, de: 1, tr: 2 }[this.i18n.language()]];
  }

  protected readonly personas: readonly DemoPersona[] = [
    {
      username: 'anna.berater',
      role: [
        'Customer advisor, branch STR',
        'Kundenberaterin, Filiale STR',
        'Müşteri danışmanı, STR şubesi',
      ],
      expectation: [
        'Stuttgart customers with masked sensitive fields; cash desk (deposits/withdrawals) for her branch; no credit access.',
        'Stuttgarter Kunden mit maskierten sensiblen Feldern; Kasse (Ein-/Auszahlungen) für ihre Filiale; kein Kreditzugriff.',
        'Hassas alanları maskeli Stuttgart müşterileri; kendi şubesi için vezne (yatırma/çekme); kredi erişimi yok.',
      ],
    },
    {
      username: 'mehmet.kredit',
      role: ['Credit analyst', 'Kreditanalyst', 'Kredi analisti'],
      expectation: [
        'Only applications assigned to him and their customers; may add notes.',
        'Nur ihm zugewiesene Anträge und deren Kunden; darf Notizen hinzufügen.',
        'Yalnızca kendisine atanan başvurular ve müşterileri; not ekleyebilir.',
      ],
    },
    {
      username: 'max.approver',
      role: [
        'Credit approver, limit 50,000 EUR',
        'Kreditgenehmiger, Limit 50.000 EUR',
        'Kredi onaylayıcı, limit 50.000 EUR',
      ],
      expectation: [
        'Approves or rejects submitted applications within his limit, always with an explicit confirmation step.',
        'Genehmigt oder lehnt eingereichte Anträge innerhalb seines Limits ab, immer mit ausdrücklicher Bestätigung.',
        'Limiti dahilindeki başvuruları onaylar veya reddeder; her zaman açık bir onay adımıyla.',
      ],
    },
    {
      username: 'lena.admin',
      role: ['Platform administrator', 'Plattform-Administratorin', 'Platform yöneticisi'],
      expectation: [
        'Platform configuration rights but no business data — admin is not a superuser. The assistant explains this; there is no operations screen yet.',
        'Konfigurationsrechte, aber keine Geschäftsdaten – Admin ist kein Superuser. Der Assistent erklärt das; eine Betriebsansicht gibt es noch nicht.',
        'Yapılandırma yetkisi var, iş verisi yok — yönetici süper kullanıcı değildir. Asistan bunu açıklar; operasyon ekranı henüz yok.',
      ],
    },
    {
      username: 'sophie.hr',
      role: ['HR', 'Personal', 'İnsan kaynakları'],
      expectation: [
        'HR documents up to strictly confidential (e.g. salary-band rules) via Documents and the assistant. Neither customers nor credit. Employee screens are not implemented yet.',
        'HR-Dokumente bis streng vertraulich (z. B. Gehaltsband-Regeln) über Dokumente und Assistent. Weder Kunden noch Kredite. Mitarbeiteransichten gibt es noch nicht.',
        'Kesin gizliye kadar İK belgeleri (ör. maaş bandı kuralları), Belgeler ve asistan üzerinden. Müşteri veya kredi yok. Çalışan ekranları henüz yok.',
      ],
    },
    {
      username: 'tom.auditor',
      role: ['Auditor', 'Prüfer', 'Denetçi'],
      expectation: [
        'Holds audit.read but no business data and no documents. The audit store and auditor screen are not implemented yet; only “My access” is shown.',
        'Hat audit.read, aber keine Geschäftsdaten und keine Dokumente. Audit-Speicher und Prüferansicht gibt es noch nicht; nur „Mein Zugriff“ wird angezeigt.',
        'audit.read yetkisi var, iş verisi ve belge yok. Denetim deposu ve denetçi ekranı henüz yok; yalnızca “Erişimim” görünür.',
      ],
    },
    {
      username: 'sarah.security',
      role: ['Security administrator', 'Sicherheitsadministratorin', 'Güvenlik yöneticisi'],
      expectation: [
        'Manages document ACLs (who may read which document); no customer or credit data.',
        'Verwaltet Dokument-ACLs (wer welches Dokument lesen darf); keine Kunden- oder Kreditdaten.',
        'Belge ACL’lerini yönetir (kim hangi belgeyi okuyabilir); müşteri veya kredi verisi yok.',
      ],
    },
    {
      username: 'oliver.ops',
      role: ['Operations operator', 'Betriebsoperator', 'Operasyon operatörü'],
      expectation: [
        'Internal and confidential operations documents (runbooks, incident playbook); no customer data and no assistant. Incident/batch screens are not implemented yet.',
        'Interne und vertrauliche Betriebsdokumente (Runbooks, Incident-Playbook); keine Kundendaten, kein Assistent. Incident-/Batch-Ansichten gibt es noch nicht.',
        'Dahili ve gizli operasyon belgeleri (runbook, olay planı); müşteri verisi ve asistan yok. Olay/toplu iş ekranları henüz yok.',
      ],
    },
    ...(
      [
        ['deniz.customer', 'C-2001', 'Deniz Kaya'],
        ['meryem.customer', 'C-2002', 'Meryem Demir'],
        ['lucas.customer', 'C-2003', 'Lucas Schneider'],
        ['thomas.customer', 'C-5001', 'Thomas Huber'],
        ['elif.customer', 'C-5003', 'Elif Yılmaz'],
        ['emilia.customer', 'C-5004', 'Emilia Fischer'],
      ] as const
    ).map(([username, id, name]) => ({
      username,
      role: [
        `Bank customer ${id} (${name}), branch MUC`,
        `Bankkunde ${id} (${name}), Filiale MUC`,
        `Banka müşterisi ${id} (${name}), MUC şubesi`,
      ] as Texts,
      expectation: CUSTOMER_EXPECTATION,
    })),
  ];
}
