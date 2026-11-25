import { Routes } from '@angular/router';
import { authGuard } from './presentation/shell/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./presentation/pages/home/home-page').then((m) => m.HomePage),
    title: 'VirtualBank',
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: 'me',
        loadComponent: () =>
          import('./presentation/pages/profile/profile-page').then((m) => m.ProfilePage),
        title: 'My access',
      },
      {
        path: 'customers',
        loadComponent: () =>
          import('./presentation/pages/customers/customers-page').then((m) => m.CustomersPage),
        title: 'Customers',
      },
      {
        path: 'customers/:customerId',
        loadComponent: () =>
          import('./presentation/pages/customer-detail/customer-detail-page').then(
            (m) => m.CustomerDetailPage,
          ),
        title: 'Customer',
      },
      {
        path: 'credit',
        loadComponent: () =>
          import('./presentation/pages/credit-applications/credit-applications-page').then(
            (m) => m.CreditApplicationsPage,
          ),
        title: 'Credit applications',
      },
      {
        path: 'credit/:applicationId',
        loadComponent: () =>
          import('./presentation/pages/credit-application-detail/credit-application-detail-page').then(
            (m) => m.CreditApplicationDetailPage,
          ),
        title: 'Credit application',
      },
      {
        path: 'banking',
        loadComponent: () =>
          import('./presentation/pages/my-banking/my-banking-page').then((m) => m.MyBankingPage),
        title: 'My banking',
      },
      {
        path: 'documents',
        loadComponent: () =>
          import('./presentation/pages/documents/documents-page').then((m) => m.DocumentsPage),
        title: 'Documents',
      },
      {
        path: 'documents/:documentId',
        loadComponent: () =>
          import('./presentation/pages/document-detail/document-detail-page').then(
            (m) => m.DocumentDetailPage,
          ),
        title: 'Document',
      },
      {
        path: 'admin/acl',
        loadComponent: () =>
          import('./presentation/pages/acl/acl-list-page').then((m) => m.AclListPage),
        title: 'Document access',
      },
      {
        path: 'admin/acl/:documentId',
        loadComponent: () =>
          import('./presentation/pages/acl/acl-editor-page').then((m) => m.AclEditorPage),
        title: 'Document access',
      },
      {
        path: 'assistant',
        loadComponent: () =>
          import('./presentation/pages/assistant/assistant-page').then((m) => m.AssistantPage),
        title: 'Assistant',
      },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('./presentation/pages/not-found/not-found-page').then((m) => m.NotFoundPage),
    title: 'Not found',
  },
];
