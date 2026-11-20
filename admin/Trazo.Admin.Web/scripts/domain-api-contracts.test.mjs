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
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
      experimentalDecorators: true,
      emitDecoratorMetadata: false
    },
    fileName: filename
  }).outputText;

  const instance = new Module(filename);
  instance.filename = filename;
  instance.paths = Module._nodeModulePaths(path.dirname(filename));
  const originalLoad = Module._load;
  Module._load = function(request, parent, isMain) {
    if (request.endsWith('/core/api-http.client') || request === './api-http.client') return { ApiHttpClient: class ApiHttpClient {} };
    if (request === '@angular/core') {
      return { Injectable: () => target => target, inject: () => http };
    }
    if (request === '@angular/common/http') {
      return { HttpClient: class HttpClient {}, HttpParams: TestHttpParams };
    }
    if (request === 'rxjs') {
      return { firstValueFrom: value => Promise.resolve(value) };
    }
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

const fiscalPath = 'src/app/features/fiscal/fiscal.api.ts';
const referralsPath = 'src/app/features/referrals/referrals.api.ts';
const intelligencePath = 'src/app/features/intelligence/intelligence.api.ts';

test('FiscalApi requests the selected year and quarter using the documented routes', async () => {
  const { http, requests } = createHttp({ year: 2026 });
  const { FiscalApi } = await loadService(fiscalPath, http);
  const api = new FiscalApi();

  assert.deepEqual(await api.getFiscalYear(2026), { year: 2026 });
  assert.equal(requests[0].method, 'GET');
  assert.equal(requests[0].url, '/api/v1/admin/fiscal/years/2026');

  await api.getFiscalQuarter(2026, 4);
  assert.equal(requests[1].url, '/api/v1/admin/fiscal/quarters/2026/4');
});

test('FiscalApi creates and updates entries with the correct HTTP verb and payload', async () => {
  const { http, requests } = createHttp({ id: 'entry-1' });
  const { FiscalApi } = await loadService(fiscalPath, http);
  const api = new FiscalApi();
  const input = { concept: 'Hosting', base: 20 };

  await api.saveFiscalEntry(null, input);
  await api.saveFiscalEntry('entry-1', input);

  assert.deepEqual(requests.map(x => [x.method, x.url]), [
    ['POST', '/api/v1/admin/fiscal/entries'],
    ['PUT', '/api/v1/admin/fiscal/entries/entry-1']
  ]);
  assert.deepEqual(requests[0].argument, input);
  assert.deepEqual(requests[1].argument, input);
});

test('FiscalApi closes and reopens a month with explicit confirmation and reason', async () => {
  const { http, requests } = createHttp({ ok: true });
  const { FiscalApi } = await loadService(fiscalPath, http);
  const api = new FiscalApi();

  await api.closeFiscalMonth('2026-10', true);
  await api.reopenFiscalMonth('2026-10', 'Correction');

  assert.equal(requests[0].url, '/api/v1/admin/fiscal/months/2026-10/close');
  assert.deepEqual(requests[0].argument, { acknowledgeWarnings: true });
  assert.equal(requests[1].url, '/api/v1/admin/fiscal/months/2026-10/reopen');
  assert.deepEqual(requests[1].argument, { reason: 'Correction' });
});

test('FiscalApi downloads the month package as a Blob', async () => {
  const blob = new Blob(['archive']);
  const { http, requests } = createHttp(blob);
  const { FiscalApi } = await loadService(fiscalPath, http);
  const result = await new FiscalApi().downloadFiscalMonth('2026-10');

  assert.equal(result, blob);
  assert.equal(requests[0].url, '/api/v1/admin/fiscal/months/2026-10/package');
  assert.deepEqual(requests[0].argument, { responseType: 'blob' });
});

test('ReferralsApi serializes summary filters as query parameters', async () => {
  const { http, requests } = createHttp({ attributed: 12 });
  const { ReferralsApi } = await loadService(referralsPath, http);
  const result = await new ReferralsApi().getReferralSummary({ from: '2026-10-01', to: '2026-10-31' });

  assert.deepEqual(result, { attributed: 12 });
  assert.equal(requests[0].url, '/api/v1/admin/referrals/summary');
  assert.deepEqual(requests[0].argument.params.toObject(), { from: '2026-10-01', to: '2026-10-31' });
});

test('ReferralsApi applies pagination defaults and preserves boolean incident filters', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { ReferralsApi } = await loadService(referralsPath, http);
  await new ReferralsApi().getReferrals({ status: 'pending', incidents: true });

  assert.equal(requests[0].url, '/api/v1/admin/referrals');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    status: 'pending', incidents: 'true', page: '1', pageSize: '25'
  });
});

test('ReferralsApi sends grant void reasons and retry requests to their dedicated endpoints', async () => {
  const { http, requests } = createHttp({ outcome: 'ok' });
  const { ReferralsApi } = await loadService(referralsPath, http);
  const api = new ReferralsApi();

  await api.voidGrant('grant-7', 'Duplicate');
  await api.retryGrant('grant-8');

  assert.equal(requests[0].url, '/api/v1/admin/referrals/grants/grant-7/void');
  assert.deepEqual(requests[0].argument, { reason: 'Duplicate' });
  assert.equal(requests[1].url, '/api/v1/admin/referrals/grants/grant-8/retry');
  assert.deepEqual(requests[1].argument, {});
});

test('IntelligenceApi uses UTC ISO bounds and includes the complete final day', async () => {
  const { http, requests } = createHttp({ rows: [] });
  const { IntelligenceApi } = await loadService(intelligencePath, http);
  await new IntelligenceApi().summary('2026-10-01', '2026-10-31');

  assert.equal(requests[0].url, '/api/v1/admin/intelligence/summary');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    from: '2026-10-01T00:00:00.000Z',
    to: '2026-11-01T00:00:00.000Z'
  });
});
