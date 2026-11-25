import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { bearerTokenInterceptor } from './bearer-token.interceptor';

describe('bearerTokenInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([bearerTokenInterceptor])),
        provideHttpClientTesting(),
        { provide: OidcSecurityService, useValue: { getAccessToken: () => of('token-123') } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the token to same-origin API calls', () => {
    http.get('/api/customers').subscribe();
    expect(backend.expectOne('/api/customers').request.headers.get('Authorization')).toBe(
      'Bearer token-123',
    );
  });

  it('never adds the token to other origins', () => {
    http.get('https://evil.example/api/customers').subscribe();
    expect(
      backend.expectOne('https://evil.example/api/customers').request.headers.has('Authorization'),
    ).toBe(false);
  });

  it('never adds the token to static files', () => {
    http.get('/config.json').subscribe();
    expect(backend.expectOne('/config.json').request.headers.has('Authorization')).toBe(false);
  });
});
