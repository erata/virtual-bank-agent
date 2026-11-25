import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideAuth } from 'angular-auth-oidc-client';
import { routes } from './app.routes';
import { AccessGateway } from './application/ports/access-gateway';
import { ActionGateway } from './application/ports/action-gateway';
import { AclGateway } from './application/ports/acl-gateway';
import { AssistantGateway } from './application/ports/assistant-gateway';
import { AuthSession } from './application/ports/auth-session';
import { CreditGateway } from './application/ports/credit-gateway';
import { CustomerGateway } from './application/ports/customer-gateway';
import { DocumentGateway } from './application/ports/document-gateway';
import { SelfServiceGateway } from './application/ports/self-service-gateway';
import { PreferenceStore } from './application/ports/preference-store';
import { OidcAuthSession } from './infrastructure/auth/oidc-auth-session';
import { bearerTokenInterceptor } from './infrastructure/auth/bearer-token.interceptor';
import { oidcConfiguration } from './infrastructure/auth/oidc-config';
import { RUNTIME_CONFIG, RuntimeConfig } from './infrastructure/config/runtime-config';
import { HttpAccessGateway } from './infrastructure/http/http-access-gateway';
import { HttpActionGateway } from './infrastructure/http/http-action-gateway';
import { HttpAclGateway } from './infrastructure/http/http-acl-gateway';
import { HttpAssistantGateway } from './infrastructure/http/http-assistant-gateway';
import { HttpCreditGateway } from './infrastructure/http/http-credit-gateway';
import { HttpCustomerGateway } from './infrastructure/http/http-customer-gateway';
import { HttpDocumentGateway } from './infrastructure/http/http-document-gateway';
import { HttpSelfServiceGateway } from './infrastructure/http/http-self-service-gateway';
import { LocalPreferenceStore } from './infrastructure/preferences/local-preference-store';

/**
 * Composition root: the only place that knows which adapter implements which port.
 * Presentation and application code depend on the abstract ports (AuthSession, CustomerGateway, CreditGateway, AssistantGateway).
 */
export function appConfig(runtime: RuntimeConfig, origin: string): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideRouter(routes, withComponentInputBinding()),
      provideHttpClient(withInterceptors([bearerTokenInterceptor])),
      provideAuth({ config: oidcConfiguration(runtime, origin) }),
      { provide: RUNTIME_CONFIG, useValue: runtime },
      OidcAuthSession,
      { provide: AuthSession, useExisting: OidcAuthSession },
      { provide: CustomerGateway, useClass: HttpCustomerGateway },
      { provide: CreditGateway, useClass: HttpCreditGateway },
      { provide: AssistantGateway, useClass: HttpAssistantGateway },
      { provide: DocumentGateway, useClass: HttpDocumentGateway },
      { provide: SelfServiceGateway, useClass: HttpSelfServiceGateway },
      { provide: AccessGateway, useClass: HttpAccessGateway },
      { provide: PreferenceStore, useClass: LocalPreferenceStore },
      { provide: AclGateway, useClass: HttpAclGateway },
      { provide: ActionGateway, useClass: HttpActionGateway },
      provideAppInitializer(() => inject(OidcAuthSession).initialize()),
    ],
  };
}
