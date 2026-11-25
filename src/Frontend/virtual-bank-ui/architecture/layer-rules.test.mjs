// Architecture rules for the Angular app, mirroring tests/architecture for the .NET code.
// Run: npm run test:architecture  (node --test, no browser, no Angular build needed)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const APP = path.resolve(import.meta.dirname, '../src/app');

function files(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const full = path.join(dir, e.name);
    return e.isDirectory()
      ? files(full)
      : e.name.endsWith('.ts') && !e.name.endsWith('.spec.ts')
        ? [full]
        : [];
  });
}

/** Imports of a file: package names, or the target layer for relative imports. */
function imports(file) {
  const source = fs.readFileSync(file, 'utf8');
  return [
    ...source.matchAll(
      /(?:import|export)\s[^'"]*?from\s+['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)/g,
    ),
  ].map((m) => {
    const spec = m[1] ?? m[2];
    if (!spec.startsWith('.')) return { spec, kind: 'package' };
    const target = path.relative(APP, path.resolve(path.dirname(file), spec));
    return { spec, kind: 'layer', layer: target.split(path.sep)[0] };
  });
}

const layerOf = (file) => path.relative(APP, file).split(path.sep)[0];
const all = files(APP);
const inLayer = (layer) => all.filter((f) => layerOf(f) === layer);
const rel = (f) => path.relative(APP, f);

function violations(layer, isForbidden) {
  return inLayer(layer).flatMap((f) =>
    imports(f)
      .filter(isForbidden)
      .map((i) => `${rel(f)} -> ${i.spec}`),
  );
}

test('domain depends on nothing (no Angular, no RxJS, no other layer)', () => {
  assert.deepEqual(
    violations('domain', (i) => i.kind === 'package' || i.layer !== 'domain'),
    [],
  );
});

test('application depends only on domain, itself and @angular/core', () => {
  assert.deepEqual(
    violations('application', (i) =>
      i.kind === 'package'
        ? i.spec !== '@angular/core'
        : !['domain', 'application'].includes(i.layer),
    ),
    [],
  );
});

test('presentation never reaches infrastructure, HTTP or the OIDC library', () => {
  assert.deepEqual(
    violations('presentation', (i) =>
      i.kind === 'package'
        ? i.spec.startsWith('@angular/common/http') || i.spec.startsWith('angular-auth-oidc-client')
        : i.layer === 'infrastructure',
    ),
    [],
  );
});

test('infrastructure never depends on presentation', () => {
  assert.deepEqual(
    violations('infrastructure', (i) => i.kind === 'layer' && i.layer === 'presentation'),
    [],
  );
});

test('only the composition root wires adapters to ports', () => {
  const wiring = all
    .filter((f) => !['infrastructure'].includes(layerOf(f)))
    .flatMap((f) =>
      imports(f)
        .filter((i) => i.kind === 'layer' && i.layer === 'infrastructure')
        .map(() => rel(f)),
    );
  assert.deepEqual([...new Set(wiring)].sort(), ['app.config.ts']);
});

test('the OIDC library is used only in the auth adapter and the composition root', () => {
  const users = all
    .filter((f) => imports(f).some((i) => i.spec === 'angular-auth-oidc-client'))
    .map(rel)
    .sort();
  assert.ok(
    users.every(
      (f) => f === 'app.config.ts' || f.startsWith(`infrastructure${path.sep}auth${path.sep}`),
    ),
    users.join(', '),
  );
});

const PREFERENCES_ADAPTER = path.join('infrastructure', 'preferences', 'local-preference-store.ts');

test('no code outside the auth adapter touches browser storage', () => {
  // One exception: the preferences adapter, constrained to the UI language code by the next test.
  const offenders = all
    .filter((f) => !rel(f).startsWith(`infrastructure${path.sep}auth${path.sep}`))
    .filter((f) => rel(f) !== PREFERENCES_ADAPTER)
    .filter((f) =>
      /\b(localStorage|sessionStorage|document\.cookie)\b/.test(fs.readFileSync(f, 'utf8')),
    )
    .map(rel);
  assert.deepEqual(offenders, []);
});

test('the preferences adapter stores nothing but the UI language', () => {
  const source = fs.readFileSync(path.join(APP, PREFERENCES_ADAPTER), 'utf8');
  const keys = [...source.matchAll(/(?:getItem|setItem)\(\s*([A-Za-z_]+)/g)].map((m) => m[1]);
  assert.ok(keys.length > 0 && keys.every((k) => k === 'LANGUAGE_KEY'), keys.join(', '));
  assert.doesNotMatch(source, /token|password|iban/i);
});

test('the UI approves only through the two-step M6 confirmation', () => {
  // No one-step approve call anywhere ...
  const oneStep = all.filter((f) => /\bapprove\s*\(|\/approve\b/i.test(fs.readFileSync(f, 'utf8'))).map(rel);
  assert.deepEqual(oneStep, []);
  // ... decisions are only PREPARED by the credit adapter ...
  const preparers = all
    .filter((f) => /\/(approval|rejection)[`'"]/.test(fs.readFileSync(f, 'utf8')))
    .map(rel);
  assert.deepEqual(preparers, [path.join('infrastructure', 'http', 'http-credit-gateway.ts')]);
  // ... and every prepared change is executed in exactly one place: the action adapter's confirm call.
  const confirmers = all.filter((f) => /\/confirm[`'"]/.test(fs.readFileSync(f, 'utf8'))).map(rel);
  assert.deepEqual(confirmers, [path.join('infrastructure', 'http', 'http-action-gateway.ts')]);
});

test('every notice code the API can emit has a user-facing text', () => {
  // Contract check across the stack: codes are declared in the .NET Application layer, texts in the UI.
  const backend = path.resolve(import.meta.dirname, '../../../VirtualBank.Application');
  const sources = [
    'Assistant/AssistantNoticeCodes.cs',
    'Credit/Authorization/CreditPolicies.cs',
    'Authorization/Notices/NoticeCatalog.cs',
  ].map((f) => fs.readFileSync(path.join(backend, f), 'utf8'));
  const codes = new Set(
    sources.flatMap((s) => [...s.matchAll(/"([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)"/g)].map((m) => m[1])),
  );
  const texts = fs.readFileSync(path.join(APP, 'presentation/shared/notice-messages.ts'), 'utf8');
  const missing = [...codes].filter((c) => !new RegExp(`\\b${c}:`).test(texts));
  assert.ok(codes.size >= 18, `expected to find the notice codes, found ${codes.size}`);
  assert.deepEqual(missing, []);
});
