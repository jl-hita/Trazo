import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { PlanPreviewRequest } from '../core/admin.models';
import type { FiscalEntryInput } from './fiscal/fiscal.models';
import { ExercisesApi } from './exercises/exercises.api';
import { LogsApi } from './logs/logs.api';
import { ReferralsApi } from './referrals/referrals.api';
import { SubscriptionsApi } from './subscriptions/subscriptions.api';
import { PlanReviewsApi } from './reviews/reviews.api';
import { WaitlistApi } from './waitlist/waitlist.api';
import { OverviewApi } from './overview/overview.api';
import { FiscalApi } from './fiscal/fiscal.api';
import { PlanningApi } from './planning/planning.api';
import { MarketingApi } from './marketing/marketing.api';
import { SettingsApi } from './settings/settings.api';
import { ErrorTicketsApi } from './error-tickets/error-tickets.api';

describe('Admin domain API services', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('ExercisesApi serializes filters and encodes exercise identifiers', async () => {
    const api = TestBed.inject(ExercisesApi);
    const result = api.list({
      page: 2, pageSize: 25, q: '  bench  ', movementPattern: 'horizontal-push',
      status: 'approved', source: 'curated', sort: 'name'
    });
    const request = http.expectOne(req => req.url === '/api/v1/admin/exercises');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('q')).toBe('bench');
    expect(request.request.params.get('movementPattern')).toBe('horizontal-push');
    expect(request.request.params.get('status')).toBe('approved');
    expect(request.request.params.get('source')).toBe('curated');
    expect(request.request.params.get('sort')).toBe('name');
    request.flush({ items: [], page: 2, pageSize: 25, total: 0 });
    await expect(result).resolves.toMatchObject({ items: [] });

    const detail = api.get('exercise/one');
    const detailRequest = http.expectOne('/api/v1/admin/exercises/exercise%2Fone');
    detailRequest.flush({ id: 'exercise/one' });
    await expect(detail).resolves.toMatchObject({ id: 'exercise/one' });
  });

  it('LogsApi includes cursor and optional filters without losing comma-separated levels', async () => {
    const result = TestBed.inject(LogsApi).list({
      pageSize: 50, levels: ['Warning', 'Error'], from: '2026-11-01', to: '2026-11-20',
      q: ' timeout ', cursor: 'next-page'
    });
    const request = http.expectOne(req => req.url === '/api/v1/admin/logs');
    expect(request.request.params.get('levels')).toBe('Warning,Error');
    expect(request.request.params.get('q')).toBe('timeout');
    expect(request.request.params.get('cursor')).toBe('next-page');
    expect(request.request.params.get('from')).toBe('2026-11-01');
    request.flush({ items: [], nextCursor: null });
    await expect(result).resolves.toMatchObject({ items: [] });
  });

  it('ReferralsApi preserves filters, defaults pagination and sends mutation reasons', async () => {
    const api = TestBed.inject(ReferralsApi);
    const list = api.getReferrals({ status: 'pending', incidents: true });
    const request = http.expectOne(req => req.url === '/api/v1/admin/referrals');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('incidents')).toBe('true');
    request.flush({ items: [] });
    await expect(list).resolves.toMatchObject({ items: [] });

    const voided = api.voidGrant('grant-1', ' duplicate ');
    const mutation = http.expectOne('/api/v1/admin/referrals/grants/grant-1/void');
    expect(mutation.request.method).toBe('POST');
    expect(mutation.request.body).toEqual({ reason: ' duplicate ' });
    mutation.flush({ outcome: 'voided' });
    await expect(voided).resolves.toEqual({ outcome: 'voided' });
  });

  it('SubscriptionsApi serializes search and status filters', async () => {
    const result = TestBed.inject(SubscriptionsApi).list({
      page: 3, pageSize: 10, q: '  user@example.com ', status: 'active',
      source: 'web', expiresFrom: '', expiresTo: '', sort: 'createdAt'
    });
    const request = http.expectOne(req => req.url === '/api/v1/admin/subscriptions');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('10');
    expect(request.request.params.get('q')).toBe('user@example.com');
    expect(request.request.params.get('status')).toBe('active');
    expect(request.request.params.get('source')).toBe('web');
    expect(request.request.params.get('sort')).toBe('createdAt');
    request.flush({ items: [], page: 3 });
    await expect(result).resolves.toMatchObject({ items: [] });
  });

  it('PlanReviewsApi applies paging and sends claim/release actions to their dedicated routes', async () => {
    const api = TestBed.inject(PlanReviewsApi);
    const list = api.list('pending', 2);
    const request = http.expectOne(req => req.url === '/api/v1/admin/plan-reviews');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.get('status')).toBe('pending');
    request.flush({ items: [] });
    await expect(list).resolves.toMatchObject({ items: [] });

    const claim = api.claim('review-1');
    const claimRequest = http.expectOne('/api/v1/admin/plan-reviews/review-1/claim');
    expect(claimRequest.request.body).toEqual({});
    claimRequest.flush(null);
    await expect(claim).resolves.toBeNull();

    const release = api.release('review-1', '   ');
    const releaseRequest = http.expectOne('/api/v1/admin/plan-reviews/review-1/release');
    expect(releaseRequest.request.body).toEqual({ reason: null });
    releaseRequest.flush(null);
    await expect(release).resolves.toBeNull();
  });

  it('WaitlistApi sends selected IDs and downloads CSV as a Blob', async () => {
    const api = TestBed.inject(WaitlistApi);
    const invite = api.invite(['wait-1', 'wait-2']);
    const inviteRequest = http.expectOne('/api/v1/admin/waitlist/invite');
    expect(inviteRequest.request.method).toBe('POST');
    expect(inviteRequest.request.body).toEqual({ ids: ['wait-1', 'wait-2'] });
    inviteRequest.flush({ invited: 2 });
    await expect(invite).resolves.toMatchObject({ invited: 2 });

    const download = api.export();
    const downloadRequest = http.expectOne('/api/v1/admin/waitlist/export.csv');
    expect(downloadRequest.request.responseType).toBe('blob');
    downloadRequest.flush(new Blob(['email'], { type: 'text/csv' }));
    await expect(download).resolves.toBeInstanceOf(Blob);
  });

  it('OverviewApi only sends refresh=true when explicitly requested', async () => {
    const api = TestBed.inject(OverviewApi);
    const result = api.overview(true);
    const request = http.expectOne(req => req.url === '/api/v1/admin/overview');
    expect(request.request.params.get('refresh')).toBe('true');
    request.flush({ generatedAt: '2026-11-21T10:00:00Z' });
    await expect(result).resolves.toMatchObject({ generatedAt: '2026-11-21T10:00:00Z' });
  });

  it('FiscalApi chooses POST versus PUT and preserves closure acknowledgement', async () => {
    const api = TestBed.inject(FiscalApi);
    const input: FiscalEntryInput = {
      kind: 'expense', date: '2026-11-20', concept: 'Consulting', counterparty: null,
      counterpartyTaxId: null, counterpartyCountry: null, invoiceNumber: null, category: 'services',
      base: 100, vatTreatment: 'standard', vatRatePct: 21, withholdingPct: null,
      deductiblePct: 100, notes: ''
    };
    const created = api.saveFiscalEntry(null, input);
    const createRequest = http.expectOne('/api/v1/admin/fiscal/entries');
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual(input);
    createRequest.flush({ id: 'entry-1' });
    await expect(created).resolves.toMatchObject({ id: 'entry-1' });

    const closed = api.closeFiscalMonth('2026-11', true);
    const closeRequest = http.expectOne('/api/v1/admin/fiscal/months/2026-11/close');
    expect(closeRequest.request.body).toEqual({ acknowledgeWarnings: true });
    closeRequest.flush({ period: '2026-11' });
    await expect(closed).resolves.toMatchObject({ period: '2026-11' });
  });

  it('PlanningApi posts the preview request unchanged', async () => {
    const payload: PlanPreviewRequest = {
      goal: 'hypertrophy', experience: 'intermediate', startDate: '2026-12-01', durationWeeks: 8,
      trainingDays: ['Monday', 'Wednesday', 'Friday'], maxSessionMinutes: 60,
      availableEquipment: ['barbell', 'dumbbell'], excludedExerciseIds: [], split: 'full-body'
    };
    const result = TestBed.inject(PlanningApi).preview(payload);
    const request = http.expectOne('/api/v1/admin/planning/preview');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({ warnings: [] });
    await expect(result).resolves.toMatchObject({ warnings: [] });
  });

  it('MarketingApi uses an exclusive end date and preserves segment filters', async () => {
    const result = TestBed.inject(MarketingApi).funnel(
      { from: '2026-11-01', to: '2026-11-30' }, 'organic'
    );
    const request = http.expectOne(req => req.url === '/api/v1/admin/marketing/funnel');
    expect(request.request.params.get('from')).toBe('2026-11-01T00:00:00Z');
    expect(request.request.params.get('to')).toBe('2026-12-01T00:00:00Z');
    expect(request.request.params.get('segment')).toBe('organic');
    request.flush({ stages: [] });
    await expect(result).resolves.toMatchObject({ stages: [] });
  });

  it('SettingsApi sends push broadcast content to the admin settings endpoint', async () => {
    const result = TestBed.inject(SettingsApi).broadcastPush('Aviso', 'Mantenimiento programado');
    const request = http.expectOne('/api/v1/admin/settings/push/broadcast');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Aviso', body: 'Mantenimiento programado' });
    request.flush({ sent: 1, failed: 0 });
    await expect(result).resolves.toMatchObject({ sent: 1 });
  });

  it('ErrorTicketsApi filters tickets and returns the backend status for its diagnostic request', async () => {
    const api = TestBed.inject(ErrorTicketsApi);
    const list = api.list('  timeout ', 'open', 'web', 2, 25);
    const request = http.expectOne(req => req.url === '/api/v1/admin/error-tickets');
    expect(request.request.params.get('q')).toBe('timeout');
    expect(request.request.params.get('status')).toBe('open');
    expect(request.request.params.get('source')).toBe('web');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [] });
    await expect(list).resolves.toMatchObject({ items: [] });

    const diagnostic = api.triggerTestException();
    const diagnosticRequest = http.expectOne('/api/v1/admin/error-tickets/test-exception');
    diagnosticRequest.flush({ message: 'Expected diagnostic error' }, {
      status: 500, statusText: 'Internal Server Error'
    });
    await expect(diagnostic).resolves.toBe(500);
  });
});
