import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AclAdministration } from '../../../application/use-cases/acl-administration';
import {
  AclEffect,
  AclEntry,
  AclRevision,
  AclSnapshot,
  AclSubjectKind,
  aclProblems,
} from '../../../domain/acl';
import { Problem } from '../../../domain/result';
import { I18n } from '../../i18n/i18n';
import { MessageKey } from '../../i18n/messages';
import { AsyncResult } from '../../shared/async-result';
import { ProblemView } from '../../shared/problem-view';

/** Hints only (from the demo realm); the API validates every value and accepts any well-formed one. */
const KNOWN_GROUPS = [
  '/Customer',
  '/Customer/Stuttgart',
  '/Customer/Munich',
  '/Credit',
  '/Credit/Analysis',
  '/Credit/Approval',
  '/HR',
  '/IT',
  '/IT/Operations',
  '/Security',
  '/Security/Audit',
];
const KNOWN_ROLES = [
  'customer-advisor',
  'credit-analyst',
  'credit-approver',
  'hr-specialist',
  'operations-operator',
  'platform-admin',
  'security-admin',
  'auditor',
  'bank-customer',
];

interface Row {
  kind: AclSubjectKind;
  value: string;
  effect: AclEffect;
}

/**
 * Edits one document's ACL. Saving sends the version that was loaded; if someone changed the ACL meanwhile the
 * API rejects the save (optimistic concurrency) and the editor reloads instead of overwriting their change.
 */
@Component({
  selector: 'ba-acl-editor-page',
  imports: [RouterLink, DatePipe, ProblemView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>
      <a routerLink="/admin/acl">{{ i18n.t('acl.back') }}</a>
    </p>
    @if (snapshot.loading()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    } @else if (snapshot.problem(); as problem) {
      <ba-problem [problem]="problem" />
    } @else if (snapshot.value(); as s) {
      <h1>{{ s.title }}</h1>
      <p class="muted">
        {{ s.documentId }} ·
        <span class="badge" [attr.data-classification]="s.classification">{{
          i18n.t(classKey(s.classification))
        }}</span>
        · {{ i18n.t('common.branch') }}: {{ s.branch ?? '—' }} · {{ i18n.t('common.version') }}
        {{ s.version }}
      </p>
      <p class="muted">{{ i18n.t('acl.rules') }}</p>

      <datalist id="acl-groups">
        @for (g of groups; track g) {
          <option [value]="g"></option>
        }
      </datalist>
      <datalist id="acl-roles">
        @for (r of roles; track r) {
          <option [value]="r"></option>
        }
      </datalist>

      <table data-testid="acl-entries">
        <thead>
          <tr>
            <th>{{ i18n.t('acl.kind') }}</th>
            <th>{{ i18n.t('acl.subject') }}</th>
            <th>{{ i18n.t('acl.effect') }}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track $index; let i = $index) {
            <tr>
              <td>
                <select
                  [value]="row.kind"
                  (change)="setKind(i, $any($event.target).value)"
                  [disabled]="saving()"
                >
                  @for (k of kinds; track k) {
                    <option [value]="k" [selected]="k === row.kind">
                      {{ i18n.t(kindKey(k)) }}
                    </option>
                  }
                </select>
              </td>
              <td>
                <input
                  class="filter"
                  [value]="row.value"
                  (input)="setValue(i, $any($event.target).value)"
                  [attr.list]="
                    row.kind === 'group' ? 'acl-groups' : row.kind === 'role' ? 'acl-roles' : null
                  "
                  [disabled]="saving() || row.kind === 'public'"
                  data-testid="acl-value"
                />
              </td>
              <td>
                <select
                  [value]="row.effect"
                  (change)="setEffect(i, $any($event.target).value)"
                  [disabled]="saving()"
                >
                  <option value="allow" [selected]="row.effect === 'allow'">
                    {{ i18n.t('acl.allow') }}
                  </option>
                  <option value="deny" [selected]="row.effect === 'deny'">
                    {{ i18n.t('acl.deny') }}
                  </option>
                </select>
              </td>
              <td>
                <button type="button" class="secondary" (click)="remove(i)" [disabled]="saving()">
                  {{ i18n.t('acl.remove') }}
                </button>
              </td>
            </tr>
          }
        </tbody>
      </table>
      <div class="doc-actions" style="margin-top: 0.75rem">
        <button
          type="button"
          class="secondary"
          (click)="add()"
          [disabled]="saving()"
          data-testid="acl-add"
        >
          {{ i18n.t('acl.add') }}
        </button>
        <button
          type="button"
          (click)="save(s)"
          [disabled]="saving() || !dirty()"
          data-testid="acl-save"
        >
          {{ i18n.t('common.save') }}
        </button>
        @if (dirty()) {
          <button type="button" class="secondary" (click)="reset(s)" [disabled]="saving()">
            {{ i18n.t('acl.discard') }}
          </button>
        }
      </div>
      @if (localProblems().length > 0) {
        <div class="notice notice-invalid" role="alert">
          @for (p of localProblems(); track p) {
            <div>{{ i18n.t(problemKey(p)) }}</div>
          }
        </div>
      }
      @if (saveProblem(); as problem) {
        <ba-problem [problem]="problem" />
      }
      @if (saved()) {
        <div class="notice notice-ok" role="status" data-testid="acl-saved">
          {{ i18n.t('acl.saved', { v: s.version }) }}
        </div>
      }

      <h2>{{ i18n.t('acl.history') }}</h2>
      @if (history.value(); as revisions) {
        <table data-testid="acl-history">
          <thead>
            <tr>
              <th>{{ i18n.t('common.version') }}</th>
              <th>{{ i18n.t('acl.changedBy') }}</th>
              <th>{{ i18n.t('acl.changedAt') }}</th>
              <th>{{ i18n.t('acl.change') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of revisions; track r.version) {
              <tr>
                <td>{{ r.version }}</td>
                <td>
                  <code>{{ r.changedBy }}</code>
                </td>
                <td>{{ r.changedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                <td>
                  <code class="small">{{ r.changesJson }}</code>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4" class="muted">{{ i18n.t('acl.noHistory') }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    }
  `,
})
export class AclEditorPage {
  private readonly acl = inject(AclAdministration);
  protected readonly i18n = inject(I18n);

  readonly documentId = input.required<string>();

  protected readonly snapshot = new AsyncResult<AclSnapshot>();
  protected readonly history = new AsyncResult<readonly AclRevision[]>();
  protected readonly rows = signal<Row[]>([]);
  protected readonly dirty = signal(false);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly saveProblem = signal<Problem | null>(null);
  protected readonly localProblems = signal<readonly string[]>([]);
  protected readonly kinds: readonly AclSubjectKind[] = ['group', 'role', 'user', 'public'];
  protected readonly groups = KNOWN_GROUPS;
  protected readonly roles = KNOWN_ROLES;

  constructor() {
    effect(() => {
      const id = this.documentId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    await this.snapshot.load(() => this.acl.read(id));
    const s = this.snapshot.value();
    if (s) this.reset(s);
    void this.history.load(() => this.acl.history(id));
  }

  protected reset(s: AclSnapshot): void {
    this.rows.set(
      s.entries.map((e) => ({ kind: e.subjectKind, value: e.subjectValue, effect: e.effect })),
    );
    this.dirty.set(false);
    this.localProblems.set([]);
  }

  private change(mutate: (rows: Row[]) => void): void {
    const next = this.rows().map((r) => ({ ...r }));
    mutate(next);
    this.rows.set(next);
    this.dirty.set(true);
    this.saved.set(false);
    this.localProblems.set([]);
  }

  protected setKind(i: number, kind: AclSubjectKind): void {
    this.change((r) => {
      r[i].kind = kind;
      if (kind === 'public') r[i].value = '*';
      else if (r[i].value === '*') r[i].value = '';
    });
  }
  protected setValue(i: number, value: string): void {
    this.change((r) => (r[i].value = value));
  }
  protected setEffect(i: number, effect: AclEffect): void {
    this.change((r) => (r[i].effect = effect));
  }
  protected add(): void {
    this.change((r) => r.push({ kind: 'group', value: '', effect: 'allow' }));
  }
  protected remove(i: number): void {
    this.change((r) => r.splice(i, 1));
  }

  protected async save(s: AclSnapshot): Promise<void> {
    const entries: AclEntry[] = this.rows().map((r) => ({
      subjectKind: r.kind,
      subjectValue: r.value.trim(),
      effect: r.effect,
    }));
    const problems = aclProblems(entries);
    this.localProblems.set(problems);
    if (problems.length > 0) return;
    this.saving.set(true);
    this.saveProblem.set(null);
    const result = await this.acl.save(s.documentId, s.version, entries);
    this.saving.set(false);
    if (result.kind === 'ok') {
      await this.load(s.documentId);
      this.saved.set(true);
    } else {
      // A version conflict is reported as invalid; the user sees the reason and can reload the latest ACL.
      this.saveProblem.set(result);
    }
  }

  protected classKey(level: number): MessageKey {
    return `docs.class.${Math.min(3, Math.max(0, level))}` as MessageKey;
  }
  protected kindKey(kind: string): MessageKey {
    return `acl.kind.${kind}` as MessageKey;
  }
  protected problemKey(p: string): MessageKey {
    return `acl.problem.${p}` as MessageKey;
  }
}
