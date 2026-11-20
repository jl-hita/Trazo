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
    if (request.endsWith('/core/api-http.client') || request === './api-http.client') return { ApiHttpClient: class ApiHttpClient {} };
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
  for (const method of ['get', 'post', 'put', 'patch', 'delete']) {
    http[method] = (url, bodyOrOptions) => {
      requests.push({ method: method.toUpperCase(), url, argument: bodyOrOptions });
      return Promise.resolve(response);
    };
  }
  return { http, requests };
}

const usersPath = 'src/app/features/users/users.api.ts';
const ticketsPath = 'src/app/features/error-tickets/error-tickets.api.ts';

test('UsersApi applies filters and pagination to the users endpoint', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { UsersApi } = await loadService(usersPath, http);
  await new UsersApi().listUsers({ q: ' ana ', role: 'admin', status: 'active', sort: 'created', page: 2, pageSize: 50 });

  assert.equal(requests[0].method, 'GET');
  assert.equal(requests[0].url, '/api/v1/admin/users');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    page: '2', pageSize: '50', q: 'ana', role: 'admin', status: 'active', sort: 'created'
  });
});

test('UsersApi preserves the confirmation email in the delete request body', async () => {
  const { http, requests } = createHttp({ deleted: true, playSubscriptionNotCancelled: false });
  const { UsersApi } = await loadService(usersPath, http);
  const result = await new UsersApi().deleteUser('user-9', 'ana@example.com');

  assert.deepEqual(result, { deleted: true, playSubscriptionNotCancelled: false });
  assert.deepEqual(requests[0], {
    method: 'DELETE',
    url: '/api/v1/admin/users/user-9',
    argument: { body: { confirmEmail: 'ana@example.com' } }
  });
});

test('UsersApi grants and revokes premium access using the expected payloads', async () => {
  const { http, requests } = createHttp({ id: 'grant-1' });
  const { UsersApi } = await loadService(usersPath, http);
  const api = new UsersApi();
  const grant = { days: 30, permanent: false, reason: 'Support' };
  await api.grantPremium('user-9', grant);
  await api.revokePremium('user-9', 'grant-1', 'Refund');

  assert.equal(requests[0].url, '/api/v1/admin/users/user-9/premium-grants');
  assert.deepEqual(requests[0].argument, grant);
  assert.equal(requests[1].url, '/api/v1/admin/users/user-9/premium-grants/grant-1/revoke');
  assert.deepEqual(requests[1].argument, { reason: 'Refund' });
});

test('ErrorTicketsApi filters ticket queries and updates status', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { ErrorTicketsApi } = await loadService(ticketsPath, http);
  const api = new ErrorTicketsApi();
  await api.list(' timeout ', 'new', 'android', 3, 25);
  await api.setStatus('ticket-4', 'resolved');

  assert.equal(requests[0].url, '/api/v1/admin/error-tickets');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    page: '3', pageSize: '25', q: 'timeout', status: 'new', source: 'android'
  });
  assert.equal(requests[1].method, 'PUT');
  assert.equal(requests[1].url, '/api/v1/admin/error-tickets/ticket-4/status');
  assert.deepEqual(requests[1].argument, { status: 'resolved' });
});

test('ErrorTicketsApi returns the HTTP status from its deliberate exception probe', async () => {
  const http = {
    post: () => Promise.reject({ status: 500 })
  };
  const { ErrorTicketsApi } = await loadService(ticketsPath, http);
  assert.equal(await new ErrorTicketsApi().triggerTestException(), 500);
});
