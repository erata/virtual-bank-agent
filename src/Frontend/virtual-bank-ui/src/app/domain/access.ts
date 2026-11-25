/**
 * Navigation derived from the caller's effective permissions as resolved by the API.
 * Hiding a menu is convenience only: every page's API call is still authorized by the server.
 */
export interface NavSection {
  readonly path: string;
  /** Stable key for the UI translation (nav.<key>). */
  readonly key: string;
  readonly label: string;
  /** Visible when the caller holds ANY of these permissions. Empty means always visible once signed in. */
  readonly anyOf: readonly string[];
}

export const NAV_SECTIONS: readonly NavSection[] = [
  { path: '/banking', key: 'banking', label: 'My banking', anyOf: ['customer.read.self'] },
  {
    path: '/customers',
    key: 'customers',
    label: 'Customers',
    anyOf: ['customer.read', 'customer.read.assigned'],
  },
  {
    path: '/credit',
    key: 'credit',
    label: 'Credit applications',
    anyOf: ['credit.read', 'credit.approve', 'credit.request'],
  },
  { path: '/documents', key: 'documents', label: 'Documents', anyOf: ['document.read'] },
  { path: '/assistant', key: 'assistant', label: 'Assistant', anyOf: ['assistant.use'] },
  { path: '/admin/acl', key: 'acl', label: 'Document access', anyOf: ['document.acl.manage'] },
  { path: '/me', key: 'me', label: 'My access', anyOf: [] },
];

export function visibleSections(permissions: readonly string[]): readonly NavSection[] {
  const held = new Set(permissions);
  return NAV_SECTIONS.filter((s) => s.anyOf.length === 0 || s.anyOf.some((p) => held.has(p)));
}
