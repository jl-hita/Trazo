import { TestBed } from '@angular/core/testing';
import { HttpHeaders, HttpParams } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiHttpClient } from './api-http.client';

describe('ApiHttpClient', () => {
  let api: ApiHttpClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    api = TestBed.inject(ApiHttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends GET parameters and returns the typed response', async () => {
    const result = api.get<{ items: number[] }>('/api/v1/users', {
      params: new HttpParams().set('page', '2').set('pageSize', '25')
    });
    const request = http.expectOne(req =>
      req.url === '/api/v1/users' && req.params.get('page') === '2' && req.params.get('pageSize') === '25'
    );

    expect(request.request.method).toBe('GET');
    request.flush({ items: [1, 2] });
    await expect(result).resolves.toEqual({ items: [1, 2] });
  });

  it('downloads binary responses as Blob', async () => {
    const result = api.get('/api/v1/export', { responseType: 'blob' });
    const request = http.expectOne('/api/v1/export');
    const payload = new Blob(['report'], { type: 'text/csv' });

    expect(request.request.method).toBe('GET');
    request.flush(payload);
    await expect(result).resolves.toBeInstanceOf(Blob);
    await expect(result).resolves.toHaveProperty('type', 'text/csv');
  });

  it('sends POST and PUT bodies without changing their contracts', async () => {
    const post = api.post<{ id: number }>('/api/v1/users', { email: 'test@example.com' });
    const postRequest = http.expectOne('/api/v1/users');
    expect(postRequest.request.method).toBe('POST');
    expect(postRequest.request.body).toEqual({ email: 'test@example.com' });
    postRequest.flush({ id: 7 });
    await expect(post).resolves.toEqual({ id: 7 });

    const put = api.put<{ saved: boolean }>('/api/v1/users/7', { displayName: 'Test' }, {
      headers: new HttpHeaders({ 'X-Admin-Test': 'yes' })
    });
    const putRequest = http.expectOne('/api/v1/users/7');
    expect(putRequest.request.method).toBe('PUT');
    expect(putRequest.request.headers.get('X-Admin-Test')).toBe('yes');
    expect(putRequest.request.body).toEqual({ displayName: 'Test' });
    putRequest.flush({ saved: true });
    await expect(put).resolves.toEqual({ saved: true });
  });

  it('sends PATCH and DELETE requests with their expected bodies', async () => {
    const patch = api.patch<{ updated: boolean }>('/api/v1/users/7', { active: false });
    const patchRequest = http.expectOne('/api/v1/users/7');
    expect(patchRequest.request.method).toBe('PATCH');
    expect(patchRequest.request.body).toEqual({ active: false });
    patchRequest.flush({ updated: true });
    await expect(patch).resolves.toEqual({ updated: true });

    const remove = api.delete<{ deleted: boolean }>('/api/v1/users/7', { body: { confirmation: 'test@example.com' } });
    const deleteRequest = http.expectOne('/api/v1/users/7');
    expect(deleteRequest.request.method).toBe('DELETE');
    expect(deleteRequest.request.body).toEqual({ confirmation: 'test@example.com' });
    deleteRequest.flush({ deleted: true });
    await expect(remove).resolves.toEqual({ deleted: true });
  });

  it('preserves HTTP errors for the caller to handle', async () => {
    const result = api.get('/api/v1/failing');
    http.expectOne('/api/v1/failing').flush(
      { message: 'Unavailable' },
      { status: 503, statusText: 'Service Unavailable' }
    );

    await expect(result).rejects.toMatchObject({ status: 503 });
  });
});
