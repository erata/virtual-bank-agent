import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
  OnInit,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AssistantChat } from '../../../application/use-cases/assistant-chat';
import {
  ASSISTANT_MESSAGE_MAX_LENGTH,
  AssistantReply,
  AssistantCapabilities,
} from '../../../domain/assistant';
import { Problem } from '../../../domain/result';
import { noticeText } from '../../shared/notice-messages';
import { ProblemView } from '../../shared/problem-view';
import { ActionCard } from '../../shared/action-card';
import { I18n } from '../../i18n/i18n';

interface Turn {
  readonly id: number;
  readonly message: string;
  readonly reply: AssistantReply;
}

/**
 * Chat with the assistant. The answer is rendered as plain text (interpolation, never innerHTML). Notices and the
 * tool trace come from the API's application code, so what was denied is shown even if the model glosses over it.
 */
@Component({
  selector: 'ba-assistant-page',
  imports: [FormsModule, RouterLink, ProblemView, ActionCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ i18n.t('assistant.title') }}</h1>
    @if (capabilities(); as caps) {
      <p class="muted" data-testid="assistant-introduction">{{ caps.introduction }}</p>
      <p class="muted">{{ i18n.t('assistant.examplesNote', { mode: caps.mode }) }}</p>
    } @else {
      <p class="muted">{{ i18n.t('assistant.loadingCaps') }}</p>
    }

    @for (turn of turns(); track turn.id; let last = $last) {
      <section class="turn" data-testid="assistant-turn" [attr.id]="last ? 'latest-turn' : null">
        <p class="question">
          <strong>{{ i18n.t('assistant.you') }}</strong> {{ turn.message }}
        </p>
        @if (turn.reply.mode === 'catalog') {
          <p class="assistant-mode-hint">{{ i18n.t('assistant.catalog') }}</p>
        }
        @if (turn.reply.mode === 'offline') {
          <p class="assistant-mode-hint">{{ i18n.t('assistant.offline') }}</p>
        }
        @if (turn.reply.answer !== null) {
          <p class="answer" data-testid="assistant-answer">{{ turn.reply.answer }}</p>
        }
        <!-- Cards the assistant prepared. The model cannot confirm them; only the user can, right here. -->
        @for (action of turn.reply.actions ?? []; track action.id) {
          <ba-action-card [action]="action" />
        }
        @for (table of turn.reply.tables ?? []; track $index) {
          <section class="assistant-results" data-testid="assistant-result-table">
            <div class="assistant-results-heading">
              <h2>{{ table.title }}</h2>
              <span class="muted">{{ i18n.t('assistant.records', { n: table.totalCount }) }}</span>
            </div>
            <div class="assistant-table-scroll">
              <table>
                <thead>
                  <tr>
                    @for (column of table.columns; track $index) {
                      <th scope="col">{{ column }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of table.rows; track $index) {
                    <tr>
                      @for (cell of row; track $index) {
                        <td>{{ cell }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (table.totalCount > table.rows.length) {
              <p class="muted">{{ i18n.t('assistant.firstRows', { n: table.rows.length }) }}</p>
            }
          </section>
        }
        @if (turn.reply.citations && turn.reply.citations.length > 0) {
          <section
            [attr.aria-label]="i18n.t('assistant.sources')"
            data-testid="assistant-citations"
          >
            <strong>{{ i18n.t('assistant.sources') }}</strong>
            <ul>
              @for (
                citation of turn.reply.citations;
                track citation.documentId + citation.section
              ) {
                <li>
                  {{ citation.title }} ({{ citation.documentId
                  }}{{
                    citation.pageNumber !== null
                      ? ', ' + i18n.t('assistant.page', { n: citation.pageNumber })
                      : ''
                  }}{{
                    citation.section && citation.section !== 'Document'
                      ? ', ' + citation.section
                      : ''
                  }})
                </li>
              }
            </ul>
          </section>
        }
        @for (code of turn.reply.notices; track code) {
          <div
            class="notice"
            [class.notice-forbidden]="
              code !== 'ASSISTANT_NO_DOCUMENT_EVIDENCE' &&
              code !== 'ASSISTANT_NO_VERIFIED_DATA' &&
              code !== 'ASSISTANT_NO_VERIFIED_FINANCIAL_DATA'
            "
            [class.notice-info]="
              code === 'ASSISTANT_NO_DOCUMENT_EVIDENCE' ||
              code === 'ASSISTANT_NO_VERIFIED_DATA' ||
              code === 'ASSISTANT_NO_VERIFIED_FINANCIAL_DATA'
            "
            role="status"
            data-testid="assistant-notice"
          >
            {{ text(code, i18n.language()) }} <code class="notice-code">{{ code }}</code>
            @if (
              code === 'ASSISTANT_HUMAN_CONFIRMATION_REQUIRED' && applicationIn(turn.message);
              as id
            ) {
              <a [routerLink]="['/credit', id]" data-testid="assistant-open-approval">{{
                i18n.t('assistant.openApproval', { id: id })
              }}</a>
            }
          </div>
        }
        @if (turn.reply.toolCalls.length > 0) {
          <details>
            <summary>
              {{
                i18n.t('assistant.toolCalls', {
                  n: turn.reply.toolCalls.length,
                  mode: turn.reply.mode,
                })
              }}
              @if (turn.reply.partial) {
                · {{ i18n.t('assistant.partial') }}
              }
            </summary>
            <table class="tool-calls" data-testid="assistant-tools">
              <thead>
                <tr>
                  <th>#</th>
                  <th>{{ i18n.t('assistant.tool') }}</th>
                  <th>{{ i18n.t('common.status') }}</th>
                  <th>{{ i18n.t('assistant.notice') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (call of turn.reply.toolCalls; track call.sequence) {
                  <tr>
                    <td>{{ call.sequence }}</td>
                    <td>
                      <code>{{ call.tool }}</code>
                    </td>
                    <td>
                      <span class="badge" [attr.data-status]="call.status">{{ call.status }}</span>
                    </td>
                    <td>{{ call.noticeCode ?? '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </details>
        }
      </section>
    }

    <!-- Never submitted implicitly (Enter in a field, picking an example): only the Send button or Ctrl+Enter sends. -->
    <form (submit)="$event.preventDefault()" class="note-form" id="assistant-form">
      @if (capabilities(); as caps) {
        @if (caps.suggestions.length > 0) {
          <!-- Picking an example only fills the box; the user reviews/edits and sends. It stays next to the input. -->
          <select
            class="suggestion-select"
            [attr.aria-label]="i18n.t('assistant.pick')"
            data-testid="assistant-suggestions"
            [disabled]="sending()"
            (change)="pick($any($event.target))"
            (keydown.enter)="$event.preventDefault()"
          >
            <option value="" selected>{{ i18n.t('assistant.pick') }}</option>
            @for (suggestion of caps.suggestions; track suggestion.tool; let i = $index) {
              <option [value]="i">{{ suggestion.label }} — {{ suggestion.prompt }}</option>
            }
          </select>
        }
      }
      <textarea
        #input
        (keydown.control.enter)="send()"
        name="message"
        rows="3"
        [maxlength]="maxLength"
        [(ngModel)]="message"
        [disabled]="sending()"
        [placeholder]="i18n.t('assistant.placeholder')"
        data-testid="assistant-input"
      ></textarea>
      <div class="row">
        <span class="muted">{{ message.length }} / {{ maxLength }}</span>
        <span class="row-actions">
          @if (sending()) {
            <button
              type="button"
              class="secondary"
              (click)="cancel()"
              data-testid="assistant-cancel"
            >
              {{ i18n.t('common.cancel') }}
            </button>
          }
          <button
            type="button"
            (click)="send()"
            [disabled]="sending()"
            data-testid="assistant-send"
          >
            {{
              !sending()
                ? i18n.t('assistant.send')
                : elapsed() > 0
                  ? i18n.t('assistant.thinking', { s: elapsed() })
                  : i18n.t('assistant.thinkingStart')
            }}
          </button>
        </span>
      </div>
    </form>
    @if (cancelled()) {
      <p class="muted" role="status" data-testid="assistant-cancelled">
        {{ i18n.t('assistant.cancelled') }}
      </p>
    }
    @if (problem(); as p) {
      <ba-problem [problem]="p" />
    }
  `,
})
export class AssistantPage implements OnInit {
  private readonly chat = inject(AssistantChat);
  protected readonly i18n = inject(I18n);

  private nextId = 1;
  private conversationId: string | null = null;

  protected readonly maxLength = ASSISTANT_MESSAGE_MAX_LENGTH;
  protected readonly capabilities = signal<AssistantCapabilities | null>(null);

  /** Optional ?q= shortcut (e.g. from a document page). It only pre-fills the box; the user still sends. */
  readonly q = input<string | undefined>();

  ngOnInit(): void {
    const prefill = this.q()?.trim();
    if (prefill) this.message = prefill.slice(0, this.maxLength);
  }

  private async loadCapabilities(): Promise<void> {
    const response = await this.chat.capabilities(this.i18n.language());
    if (response.kind === 'ok') this.capabilities.set(response.value);
  }

  protected readonly turns = signal<readonly Turn[]>([]);

  protected readonly sending = signal(false);

  protected readonly problem = signal<Problem | null>(null);

  protected message = '';

  protected readonly text = noticeText;

  /** Seconds since the current request started, so a slow model is distinguishable from a stuck page. */
  protected readonly elapsed = signal(0);

  private timer: ReturnType<typeof setInterval> | null = null;

  /** Aborts the in-flight request (the API stops the model call and stores nothing). */
  private inFlight: AbortController | null = null;

  protected readonly cancelled = signal(false);

  /** The application the user named (e.g. "approve K-100"), to link to the page where a human confirms. */
  protected applicationIn(message: string): string | null {
    return /K-[0-9]{3}/i.exec(message)?.[0].toUpperCase() ?? null;
  }

  protected cancel(): void {
    this.inFlight?.abort();
  }

  constructor() {
    // Introduction and example questions come from the API in the UI language; reload them when it changes.
    effect(() => {
      this.i18n.language();
      untracked(() => void this.loadCapabilities());
    });
    inject(DestroyRef).onDestroy(() => {
      this.inFlight?.abort(); // leaving the page also stops a running model call
      this.stopTimer();
    });
  }

  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');

  /** Fills the box with the chosen example and resets the picker; nothing is sent until the user presses Send. */
  protected pick(select: HTMLSelectElement): void {
    const suggestion = this.capabilities()?.suggestions[Number(select.value)];
    select.value = '';
    if (!suggestion) return;
    this.message = suggestion.prompt;
    const box = this.input()?.nativeElement;
    if (box) {
      box.value = suggestion.prompt; // OnPush + plain field: write through so the box shows it immediately
      box.focus();
    }
  }

  protected async send(): Promise<void> {
    if (this.sending()) return;
    const message = this.message;
    const controller = new AbortController();
    this.inFlight = controller;
    this.sending.set(true);
    this.cancelled.set(false);
    this.problem.set(null);
    this.elapsed.set(0);
    const started = Date.now();
    this.timer = setInterval(
      () => this.elapsed.set(Math.floor((Date.now() - started) / 1000)),
      1000,
    );
    try {
      // Hard upper bound (slightly above the API/nginx timeouts): the button can never stay in "thinking".
      const timeout = setTimeout(() => (this.timedOut = true), 330_000);
      const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(330_000)]);
      const result = await this.chat
        .ask(message, this.conversationId, signal)
        .finally(() => clearTimeout(timeout));
      if (this.timedOut) {
        this.timedOut = false;
        this.problem.set({ kind: 'unavailable' } as Problem);
      } else if (controller.signal.aborted) {
        this.cancelled.set(true); // keep the text so the user can edit and resend
      } else if (result.kind === 'ok') {
        this.conversationId = result.value.conversationId ?? null;
        this.turns.update((t) => [
          ...t,
          { id: this.nextId++, message: message.trim(), reply: result.value },
        ]);
        this.message = '';
        // Bring the new answer into view; the input (with the example picker) follows right below it.
        setTimeout(() =>
          document
            .getElementById('latest-turn')
            ?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
        );
      } else {
        this.problem.set(result);
      }
    } finally {
      // Always re-enable the form, whatever happened to the request.
      this.inFlight = null;
      this.stopTimer();
      this.sending.set(false);
    }
  }

  private timedOut = false;

  private stopTimer(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }
}
