/** Document ACL administration (metadata only: an ACL admin never sees document content). */
export type AclSubjectKind = 'public' | 'user' | 'group' | 'role';
export type AclEffect = 'allow' | 'deny';

export interface AclEntry {
  readonly subjectKind: AclSubjectKind;
  readonly subjectValue: string;
  readonly effect: AclEffect;
}

export interface AclIndexItem {
  readonly documentId: string;
  readonly title: string;
  readonly version: number;
  readonly classification: number;
  readonly branch: string | null;
  readonly entryCount: number;
}

export interface AclSnapshot {
  readonly documentId: string;
  readonly title: string;
  readonly version: number;
  readonly entries: readonly AclEntry[];
  readonly classification: number;
  readonly branch: string | null;
}

export interface AclRevision {
  readonly version: number;
  readonly changedBy: string;
  readonly changedAt: string;
  readonly changesJson: string;
}

const GROUP_PATH = /^(\/[A-Za-z0-9][A-Za-z0-9 _-]{0,49}){1,5}$/;

/**
 * Same rules as the API (DocumentAclAdministration.UpdateAsync), checked before the round trip.
 * Returns a list of problems; empty means valid. The API re-validates everything.
 */
export function aclProblems(entries: readonly AclEntry[]): readonly string[] {
  const problems: string[] = [];
  if (entries.length < 1 || entries.length > 100) problems.push('count');
  const seen = new Set<string>();
  for (const e of entries) {
    const value = e.subjectValue.trim();
    if (!value || value.length > 150) problems.push('value');
    if (e.subjectKind === 'public' && value !== '*') problems.push('public');
    if (e.subjectKind === 'group' && !GROUP_PATH.test(value)) problems.push('group');
    const key = `${e.subjectKind}|${value}|${e.effect}`;
    if (seen.has(key)) problems.push('duplicate');
    seen.add(key);
  }
  return [...new Set(problems)];
}
