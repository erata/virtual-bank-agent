import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { PendingAction } from '../../domain/actions';
import { CreditApplication, CreditNote, CreditNoteEntry } from '../../domain/credit-application';
import { Account, CustomerDetail, CustomerSummary } from '../../domain/customer';
import { Result, ok } from '../../domain/result';
import { CreditGateway } from '../ports/credit-gateway';
import { CustomerGateway } from '../ports/customer-gateway';
import { CreditWorkspace } from './credit-workspace';
import { CustomerDirectory } from './customer-directory';

class RecordingCustomerGateway extends CustomerGateway {
  calls: string[] = [];
  list(): Promise<Result<readonly CustomerSummary[]>> {
    this.calls.push('list');
    return Promise.resolve(ok([]));
  }
  get(id: string): Promise<Result<CustomerDetail>> {
    this.calls.push(`get ${id}`);
    return Promise.resolve({ kind: 'not-found' });
  }
  accounts(id: string): Promise<Result<readonly Account[]>> {
    this.calls.push(`accounts ${id}`);
    return Promise.resolve(ok([]));
  }
  prepareCash(
    customerId: string,
    accountId: string,
    amount: number,
    deposit: boolean,
  ): Promise<Result<PendingAction>> {
    this.calls.push(`cash ${customerId} ${accountId} ${amount} ${deposit}`);
    return Promise.resolve({ kind: 'not-found' });
  }
}

class RecordingCreditGateway extends CreditGateway {
  calls: string[] = [];
  list(): Promise<Result<readonly CreditApplication[]>> {
    this.calls.push('list');
    return Promise.resolve(ok([]));
  }
  get(id: string): Promise<Result<CreditApplication>> {
    this.calls.push(`get ${id}`);
    return Promise.resolve({ kind: 'not-found' });
  }
  addNote(id: string, text: string): Promise<Result<CreditNote>> {
    this.calls.push(`note ${id} ${text}`);
    return Promise.resolve(ok({ id: 1, applicationId: id, createdAt: '2026-01-01T00:00:00Z' }));
  }
  notes(id: string): Promise<Result<readonly CreditNoteEntry[]>> {
    this.calls.push(`notes ${id}`);
    return Promise.resolve(ok([]));
  }
  prepareApproval(id: string): Promise<Result<PendingAction>> {
    this.calls.push(`prepare ${id}`);
    return Promise.resolve({ kind: 'not-found' });
  }
  prepareCreditRequest(
    amount: number,
    term: number,
    purpose: string,
  ): Promise<Result<PendingAction>> {
    this.calls.push(`request ${amount} ${term} ${purpose}`);
    return Promise.resolve({ kind: 'not-found' });
  }
  prepareRejection(id: string, reason: string): Promise<Result<PendingAction>> {
    this.calls.push(`reject ${id} ${reason}`);
    return Promise.resolve({ kind: 'not-found' });
  }
}

describe('use cases', () => {
  let customers: RecordingCustomerGateway;
  let credit: RecordingCreditGateway;

  beforeEach(() => {
    customers = new RecordingCustomerGateway();
    credit = new RecordingCreditGateway();
    TestBed.configureTestingModule({
      providers: [
        { provide: CustomerGateway, useValue: customers },
        { provide: CreditGateway, useValue: credit },
      ],
    });
  });

  it('answers a malformed customer id with not-found and no request', async () => {
    const result = await TestBed.inject(CustomerDirectory).getCustomer('C-1002/../admin');
    expect(result.kind).toBe('not-found');
    expect(customers.calls).toEqual([]);
  });

  it('passes a valid customer id to the gateway', async () => {
    await TestBed.inject(CustomerDirectory).listAccounts('C-1002');
    expect(customers.calls).toEqual(['accounts C-1002']);
  });

  it('rejects a blank note locally', async () => {
    const result = await TestBed.inject(CreditWorkspace).addNote('K-100', '   ');
    expect(result.kind).toBe('invalid');
    expect(credit.calls).toEqual([]);
  });

  it('sends the trimmed note text', async () => {
    const result = await TestBed.inject(CreditWorkspace).addNote('K-100', '  Income verified.  ');
    expect(result.kind).toBe('ok');
    expect(credit.calls).toEqual(['note K-100 Income verified.']);
  });

  it('answers a malformed application id with not-found and no request', async () => {
    const result = await TestBed.inject(CreditWorkspace).addNote('K-100/approve', 'text');
    expect(result.kind).toBe('not-found');
    expect(credit.calls).toEqual([]);
  });

  it('a rejection needs a reason before anything reaches the API; malformed ids never do', async () => {
    const workspace = TestBed.inject(CreditWorkspace);
    expect((await workspace.prepareRejection('K-100', '   ')).kind).toBe('invalid');
    expect((await workspace.prepareApproval('K-1000', '')).kind).toBe('not-found');
    expect(credit.calls.filter((c) => c.startsWith('reject') || c.startsWith('prepare'))).toEqual(
      [],
    );
    await workspace.prepareRejection('K-100', ' income not verifiable ');
    expect(credit.calls).toContain('reject K-100 income not verifiable');
  });
});
