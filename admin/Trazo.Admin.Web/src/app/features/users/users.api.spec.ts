import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ApiHttpClient } from '../../core/api-http.client';
import { UsersApi } from './users.api';

describe('UsersApi', () => {
  let api: UsersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(UsersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('trims search filters and preserves pagination and sort query parameters', async () => {
    const result = api.listUsers({
      q: '  alice@example.com  ', role: 'admin', status: 'active', sort: 'created', page: 2, pageSize: 25
    });
    const request = http.expectOne(req => req.url === '/api/v1/admin/users');

    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize', 'q', 'role', 'sort', 'status']);
    expect(request.request.params.get('q')).toBe('alice@example.com');
    expect(request.request.params.get('role')).toBe('admin');
    expect(request.request.params.get('status')).toBe('active');
    expect(request.request.params.get('sort')).toBe('created');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush({ items: [], page: 2, pageSize: 25, totalCount: 0, totalPages: 1 });
    await expect(result).resolves.toMatchObject({ page: 2, totalCount: 0 });
  });

  it('keeps the email confirmation in the delete request body', async () => {
    const result = api.deleteUser('user-7', 'alice@example.com');
    const request = http.expectOne('/api/v1/admin/users/user-7');

    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toEqual({ confirmEmail: 'alice@example.com' });
    request.flush({ deleted: true, playSubscriptionNotCancelled: false });
    await expect(result).resolves.toEqual({ deleted: true, playSubscriptionNotCancelled: false });
  });

  it('uses the selected action as the endpoint suffix', async () => {
    const result = api.userAction('user-7', 'revoke-sessions');
    const request = http.expectOne('/api/v1/admin/users/user-7/revoke-sessions');

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    request.flush({ id: 'user-7' });
    await expect(result).resolves.toMatchObject({ id: 'user-7' });
  });
});
