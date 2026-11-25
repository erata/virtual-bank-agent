import { Injectable, computed, inject, signal } from '@angular/core';
import { NavSection, visibleSections } from '../../domain/access';
import { AccessGateway } from '../ports/access-gateway';

/**
 * Loads the caller's permissions once per sign-in and exposes the menu sections they can use.
 * Until the answer arrives (or if it fails) only the always-visible sections are shown.
 */
@Injectable({ providedIn: 'root' })
export class AccessNavigation {
  private readonly gateway = inject(AccessGateway);
  private readonly permissions = signal<readonly string[]>([]);
  private loadedFor: string | null = null;

  /** The caller's effective permissions as resolved by the API (empty until loaded). */
  readonly granted = this.permissions.asReadonly();

  readonly sections = computed<readonly NavSection[]>(() => visibleSections(this.permissions()));

  async loadFor(subject: string | null): Promise<void> {
    if (subject === this.loadedFor) return;
    this.loadedFor = subject;
    this.permissions.set([]);
    if (subject === null) return;
    const result = await this.gateway.permissions();
    if (result.kind === 'ok' && this.loadedFor === subject) this.permissions.set(result.value);
  }
}
