import { Signal, computed, signal } from '@angular/core';
import { Problem, Result } from '../../domain/result';

/** Loading/success/problem state for one use-case call, as signals for templates. */
export class AsyncResult<T> {
  private readonly state = signal<Result<T> | null>(null);

  readonly loading: Signal<boolean> = computed(() => this.state() === null);

  readonly value: Signal<T | null> = computed(() => {
    const s = this.state();
    return s?.kind === 'ok' ? s.value : null;
  });

  readonly problem: Signal<Problem | null> = computed(() => {
    const s = this.state();
    return s !== null && s.kind !== 'ok' ? s : null;
  });

  async load(run: () => Promise<Result<T>>): Promise<void> {
    this.state.set(null);
    this.state.set(await run());
  }
}
