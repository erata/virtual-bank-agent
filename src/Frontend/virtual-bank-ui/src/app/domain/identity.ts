/**
 * The signed-in user as shown by the UI. Derived from the access token for DISPLAY ONLY:
 * every authorization decision is made again by the APIs (policies) and the database (RLS).
 */
export interface Identity {
  readonly subject: string;
  readonly username: string;
  readonly displayName: string;
  readonly roles: readonly string[];
  readonly groups: readonly string[];
  readonly branch: string | null;
  readonly department: string | null;
  readonly approvalLimit: number | null;
  readonly expiresAt: Date | null;
}

export type AuthState =
  | { readonly status: 'checking' }
  | { readonly status: 'anonymous' }
  | { readonly status: 'authenticated'; readonly identity: Identity };
