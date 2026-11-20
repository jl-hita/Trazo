import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SetupApi, type AdminAccount, type EmailSettings, type GoogleSettings } from './setup.api';

describe('SetupApi', () => {
  let api: SetupApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    api = TestBed.inject(SetupApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('reads setup status and completes first-run configuration with the established payload shape', async () => {
    const status = api.status();
    const statusRequest = http.expectOne('/api/v1/setup/status');
    expect(statusRequest.request.method).toBe('GET');
    statusRequest.flush({ setupRequired: true, needsSuperadmin: true });
    await expect(status).resolves.toMatchObject({ setupRequired: true, needsSuperadmin: true });

    const google: GoogleSettings = { clientId: 'google-client-id' };
    const email: EmailSettings = {
      host: 'smtp.example.com', port: 587, enableSsl: true, username: 'mailer',
      password: null, from: 'noreply@example.com', actionBaseUrl: 'https://example.com', enabled: false
    };
    const admin: AdminAccount = {
      email: 'admin@example.com', displayName: 'Admin', password: 'StrongPassword123!'
    };
    const complete = api.complete(google, email, admin);
    const completeRequest = http.expectOne('/api/v1/setup/complete');
    expect(completeRequest.request.method).toBe('POST');
    expect(completeRequest.request.body).toEqual({ google, email, admin });
    completeRequest.flush({ completed: true });
    await expect(complete).resolves.toEqual({ completed: true });
  });

  it('uses the public setup and admin settings routes for their respective SMTP and Google tests', async () => {
    const publicTest = api.testGoogle('google-client-id');
    const publicRequest = http.expectOne('/api/v1/setup/test/google');
    expect(publicRequest.request.body).toEqual({ clientId: 'google-client-id' });
    publicRequest.flush({ ok: true, message: 'OK' });
    await expect(publicTest).resolves.toMatchObject({ ok: true });

    const adminTest = api.testGoogle('google-client-id', true);
    const adminRequest = http.expectOne('/api/v1/admin/settings/test/google');
    expect(adminRequest.request.body).toEqual({ clientId: 'google-client-id' });
    adminRequest.flush({ ok: true, message: 'OK' });
    await expect(adminTest).resolves.toMatchObject({ ok: true });
  });
});
