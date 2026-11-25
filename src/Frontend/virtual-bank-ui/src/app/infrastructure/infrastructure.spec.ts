import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { toIdentity } from './auth/claims';
import { isApiRequest } from './auth/bearer-token.interceptor';
import { oidcConfiguration } from './auth/oidc-config';
import { parseRuntimeConfig } from './config/runtime-config';
import { toProblem } from './http/problem-mapper';

const error = (status: number, body: unknown = null) =>
  new HttpErrorResponse({ status, error: body });

describe('problem mapping', () => {
  it('keeps a well-formed notice code from a 403', () => {
    expect(
      toProblem(error(403, { title: 'Forbidden', noticeCode: 'CREDIT_READ_REQUIRED' })),
    ).toEqual({
      kind: 'forbidden',
      noticeCode: 'CREDIT_READ_REQUIRED',
    });
  });

  it('never surfaces free text from the server as a notice code', () => {
    expect(toProblem(error(403, { noticeCode: '<img src=x onerror=alert(1)>' }))).toEqual({
      kind: 'forbidden',
      noticeCode: null,
    });
  });

  it.each([
    [401, 'unauthenticated'],
    [404, 'not-found'],
    [500, 'unavailable'],
    [0, 'unavailable'],
  ])('maps HTTP %s to %s', (status, kind) => {
    expect(toProblem(error(status)).kind).toBe(kind);
  });

  it('maps validation problems field by field', () => {
    expect(toProblem(error(400, { errors: { text: ['too long'] } }))).toEqual({
      kind: 'invalid',
      errors: { text: ['too long'] },
    });
  });

  it('treats non-HTTP failures as unavailable', () => {
    expect(toProblem(new Error('boom')).kind).toBe('unavailable');
  });
});

describe('bearer token scope', () => {
  it.each([
    ['/api/customers', true],
    ['/api/credit/applications/K-100', true],
    ['http://localhost:8080/realms/virtual-bank/protocol/openid-connect/token', false],
    ['https://evil.example/api/customers', false],
    ['//evil.example/api/customers', false],
    ['/config.json', false],
  ])('%s gets the token: %s', (url, expected) => {
    expect(isApiRequest(url)).toBe(expected);
  });
});

describe('claims → identity (display only)', () => {
  it('maps Keycloak claims and hides technical roles', () => {
    const identity = toIdentity({
      sub: 'u1',
      preferred_username: 'max.approver',
      name: 'Max Approver',
      realm_access: { roles: ['credit-approver', 'offline_access', 'default-roles-virtual-bank'] },
      groups: ['/Credit/Approval'],
      approval_limit: '50000',
      exp: 1_700_000_000,
    });
    expect(identity.roles).toEqual(['credit-approver']);
    expect(identity.approvalLimit).toBe(50000);
    expect(identity.displayName).toBe('Max Approver');
    expect(identity.expiresAt?.getTime()).toBe(1_700_000_000_000);
  });

  it('turns malformed claims into "not set" instead of failing', () => {
    const identity = toIdentity({
      sub: 'u1',
      realm_access: 'nope',
      groups: 'x',
      approval_limit: 'a lot',
    });
    expect(identity.roles).toEqual([]);
    expect(identity.groups).toEqual([]);
    expect(identity.approvalLimit).toBeNull();
  });
});

describe('runtime configuration', () => {
  const valid = {
    keycloakUrl: 'http://localhost:8080/',
    realm: 'virtual-bank',
    clientId: 'virtual-bank-ui',
  };

  it('accepts the local development settings and normalizes the URL', () => {
    expect(parseRuntimeConfig(valid).keycloakUrl).toBe('http://localhost:8080');
  });

  it('requires https outside localhost', () => {
    expect(() => parseRuntimeConfig({ ...valid, keycloakUrl: 'http://sso.example.com' })).toThrow(
      /https/,
    );
    expect(
      parseRuntimeConfig({ ...valid, keycloakUrl: 'https://sso.example.com' }).keycloakUrl,
    ).toBe('https://sso.example.com');
  });

  it.each([
    {},
    null,
    { ...valid, realm: '' },
    { ...valid, realm: '../master' },
    { ...valid, clientId: 'a b' },
  ])('rejects %o', (raw) => {
    expect(() => parseRuntimeConfig(raw)).toThrow();
  });

  it('builds a PKCE code-flow configuration with refresh tokens and no session iframe', () => {
    const config = oidcConfiguration(parseRuntimeConfig(valid), 'http://localhost:4200');
    expect(config.authority).toBe('http://localhost:8080/realms/virtual-bank');
    expect(config.responseType).toBe('code');
    expect(config.useRefreshToken).toBe(true);
    expect(config.startCheckSession).toBe(false);
    expect(config.redirectUrl).toBe('http://localhost:4200/');
  });
});
