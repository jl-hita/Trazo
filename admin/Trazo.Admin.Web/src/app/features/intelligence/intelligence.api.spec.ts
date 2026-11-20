import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { IntelligenceApi } from './intelligence.api';

describe('IntelligenceApi', () => {
  let api: IntelligenceApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(IntelligenceApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('requests a summary with an inclusive start date and exclusive end date in UTC', async () => {
    const result = api.summary('2026-11-01', '2026-11-30');
    const request = http.expectOne(req => req.url === '/api/v1/admin/intelligence/summary');

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('from')).toBe('2026-11-01T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-12-01T00:00:00.000Z');
    request.flush({ from: '2026-11-01', generatedAt: '2026-11-30T12:00:00Z' });
    await expect(result).resolves.toMatchObject({ from: '2026-11-01' });
  });

  it('downloads the Intelligence export as a binary ZIP response', async () => {
    const result = api.export('2026-11-01', '2026-11-30');
    const request = http.expectOne(req => req.url === '/api/v1/admin/intelligence/export');
    const payload = new Blob(['PK\\x03\\x04'], { type: 'application/zip' });

    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.get('from')).toBe('2026-11-01T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-12-01T00:00:00.000Z');
    request.flush(payload);
    await expect(result).resolves.toBeInstanceOf(Blob);
  });
});
