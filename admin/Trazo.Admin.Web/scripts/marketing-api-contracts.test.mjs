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

async function loadService(http) {
  const filename = path.resolve(process.cwd(), 'src/app/features/marketing/marketing.api.ts');
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
    if (request === '../../core/setup.api') return { errorMessage: () => 'Request failed' };
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

test('MarketingApi applies an exclusive UTC end bound to summary and strategies reports', async () => {
  const { http, requests } = createHttp({ kpis: [] });
  const { MarketingApi } = await loadService(http);
  const api = new MarketingApi();
  const range = { from: '2026-11-01', to: '2026-11-30' };
  await api.summary(range);
  await api.strategies(range);

  const expected = { from: '2026-11-01T00:00:00Z', to: '2026-12-01T00:00:00Z' };
  assert.equal(requests[0].url, '/api/v1/admin/marketing/summary');
  assert.deepEqual(requests[0].argument.params.toObject(), expected);
  assert.equal(requests[1].url, '/api/v1/admin/marketing/strategies');
  assert.deepEqual(requests[1].argument.params.toObject(), expected);
});

test('MarketingApi omits an empty segment filter and serializes a selected segment', async () => {
  const { http, requests } = createHttp({ stages: [] });
  const { MarketingApi } = await loadService(http);
  const api = new MarketingApi();
  const range = { from: '2026-11-01', to: '2026-11-30' };
  await api.funnel(range, '');
  await api.funnel(range, 'organic');

  assert.deepEqual(requests[0].argument.params.toObject(), {
    from: '2026-11-01T00:00:00Z', to: '2026-12-01T00:00:00Z'
  });
  assert.deepEqual(requests[1].argument.params.toObject(), {
    from: '2026-11-01T00:00:00Z', to: '2026-12-01T00:00:00Z', segment: 'organic'
  });
});

test('MarketingApi downloads strategies as a Blob and persists download counts with a typed payload', async () => {
  const { http, requests } = createHttp(new Blob(['csv']));
  const { MarketingApi } = await loadService(http);
  const api = new MarketingApi();
  const range = { from: '2026-11-01', to: '2026-11-30' };
  await api.strategiesCsv(range);
  await api.saveDownloads({ week: '2026-11-02', downloads: 120, note: 'Store report' });

  assert.deepEqual(requests[0].argument, {
    params: new TestHttpParams().set('from', '2026-11-01T00:00:00Z').set('to', '2026-12-01T00:00:00Z'),
    responseType: 'blob'
  });
  assert.equal(requests[1].method, 'PUT');
  assert.equal(requests[1].url, '/api/v1/admin/marketing/downloads');
  assert.deepEqual(requests[1].argument, { week: '2026-11-02', downloads: 120, note: 'Store report' });
});

test('MarketingApi keeps nullable rates and formats zero-safe values consistently', async () => {
  const { http } = createHttp();
  const { MarketingApi, fmt, marketingError, nextDay } = await loadService(http);

  assert.equal(nextDay('2026-12-31'), '2027-01-01');
  assert.equal(fmt.pct(null), '—');
  assert.equal(fmt.eur(null), '—');
  assert.equal(fmt.delta(0, 0), '—');
  assert.equal(fmt.delta(3, 0), 'new');
  assert.equal(marketingError({ error: { message: 'Specific API error' } }), 'Specific API error');
  assert.equal(marketingError(new Error('generic')), 'Request failed');
});
