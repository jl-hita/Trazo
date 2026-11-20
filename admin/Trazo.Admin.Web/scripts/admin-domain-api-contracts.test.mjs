import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import Module from 'node:module';
import ts from 'typescript';

class TestHttpParams {
  values = new Map();
  set(key, value) { this.values.set(key, String(value)); return this; }
  toObject() { return Object.fromEntries(this.values); }
}

async function loadService(relativePath, http) {
  const filename = path.resolve(process.cwd(), relativePath);
  const source = await readFile(filename, 'utf8');
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    fileName: filename
  }).outputText;
  const instance = new Module(filename);
  instance.filename = filename;
  instance.paths = Module._nodeModulePaths(path.dirname(filename));
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) {
    if (request === '@angular/core') return { Injectable: () => target => target, inject: () => http };
    if (request === '@angular/common/http') return { HttpClient: class HttpClient {}, HttpParams: TestHttpParams };
    if (request === 'rxjs') return { firstValueFrom: value => Promise.resolve(value) };
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    instance._compile(compiled, filename);
    return instance.exports;
  } finally {
    Module._load = originalLoad;
  }
}

function createHttp(response = { ok: true }) {
  const requests = [];
  const http = {};
  for (const method of ['get', 'post', 'put', 'delete']) {
    http[method] = (url, bodyOrOptions) => {
      requests.push({ method: method.toUpperCase(), url, argument: bodyOrOptions });
      return Promise.resolve(response);
    };
  }
  return { http, requests };
}

test('OverviewApi uses the refresh flag only when requested and exposes system information', async () => {
  const { http, requests } = createHttp({ ok: true });
  const { OverviewApi } = await loadService('src/app/features/overview/overview.api.ts', http);
  const api = new OverviewApi();
  await api.overview();
  await api.overview(true);
  await api.systemInfo();

  assert.equal(requests[0].url, '/api/v1/admin/overview');
  assert.deepEqual(requests[0].argument.params.toObject(), {});
  assert.deepEqual(requests[1].argument.params.toObject(), { refresh: 'true' });
  assert.equal(requests[2].url, '/api/v1/admin/overview/system');
});

test('LogsApi serializes levels, text, date bounds, and cursor', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { LogsApi } = await loadService('src/app/features/logs/logs.api.ts', http);
  await new LogsApi().list({
    pageSize: 50, levels: ['warning', 'error'], from: '2026-11-01', to: null,
    q: ' database ', cursor: 'next-page'
  });

  assert.equal(requests[0].url, '/api/v1/admin/logs');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    pageSize: '50', levels: 'warning,error', from: '2026-11-01', q: 'database', cursor: 'next-page'
  });
});

test('PlanReviewsApi handles pagination and normalizes optional release/close messages', async () => {
  const { http, requests } = createHttp({ ok: true });
  const { PlanReviewsApi } = await loadService('src/app/features/reviews/reviews.api.ts', http);
  const api = new PlanReviewsApi();
  await api.list('pending', 2);
  await api.release('review-1', '  ');
  await api.close('review-2', '  Reviewed  ');

  assert.deepEqual(requests[0].argument.params.toObject(), { page: '2', pageSize: '25', status: 'pending' });
  assert.equal(requests[1].url, '/api/v1/admin/plan-reviews/review-1/release');
  assert.deepEqual(requests[1].argument, { reason: null });
  assert.equal(requests[2].url, '/api/v1/admin/plan-reviews/review-2/review');
  assert.deepEqual(requests[2].argument, { message: 'Reviewed' });
});

test('SubscriptionsApi converts inclusive calendar filters to API timestamps', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { SubscriptionsApi } = await loadService('src/app/features/subscriptions/subscriptions.api.ts', http);
  await new SubscriptionsApi().list({
    q: ' user ', status: 'active', source: 'google_play',
    expiresFrom: '2026-11-01', expiresTo: '2026-11-30', sort: 'expires',
    page: 3, pageSize: 25
  });

  assert.equal(requests[0].url, '/api/v1/admin/subscriptions');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    page: '3', pageSize: '25', q: 'user', status: 'active', source: 'google_play',
    expiresFrom: '2026-11-01T00:00:00.000Z', expiresTo: '2026-11-30T23:59:59.000Z', sort: 'expires'
  });
});

test('SettingsApi keeps settings in their domain routes and sends push payloads explicitly', async () => {
  const { http, requests } = createHttp({ saved: true });
  const { SettingsApi } = await loadService('src/app/features/settings/settings.api.ts', http);
  const api = new SettingsApi();
  const push = { title: 'Maintenance', body: 'Update available' };
  await api.savePush({ projectId: 'p', applicationId: 'a', apiKey: 'k', senderId: '', serviceAccountJson: null });
  await api.broadcastPush(push.title, push.body);
  await api.testDietoExpress('https://dietoexpress.example');

  assert.equal(requests[0].url, '/api/v1/admin/settings/push');
  assert.equal(requests[0].method, 'PUT');
  assert.equal(requests[1].url, '/api/v1/admin/settings/push/broadcast');
  assert.deepEqual(requests[1].argument, push);
  assert.equal(requests[2].url, '/api/v1/admin/settings/operations/test/dietoexpress');
  assert.deepEqual(requests[2].argument, { endpoint: 'https://dietoexpress.example' });
});

test('WaitlistApi filters entries, sends invitation IDs, and downloads CSV as a Blob', async () => {
  const blob = new Blob(['email']);
  const { http, requests } = createHttp(blob);
  const { WaitlistApi } = await loadService('src/app/features/waitlist/waitlist.api.ts', http);
  const api = new WaitlistApi();
  await api.list(' test ', 2, 50, 'confirmed');
  await api.invite(['a', 'b']);
  const downloaded = await api.export();

  assert.deepEqual(requests[0].argument.params.toObject(), {
    page: '2', pageSize: '50', q: 'test', status: 'confirmed'
  });
  assert.equal(requests[1].url, '/api/v1/admin/waitlist/invite');
  assert.deepEqual(requests[1].argument, { ids: ['a', 'b'] });
  assert.equal(requests[2].url, '/api/v1/admin/waitlist/export.csv');
  assert.deepEqual(requests[2].argument, { responseType: 'blob' });
  assert.equal(downloaded, blob);
});
