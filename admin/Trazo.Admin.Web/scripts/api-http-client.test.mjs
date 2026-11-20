import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import Module from 'node:module';
import ts from 'typescript';

async function loadClient(http) {
  const relativePath = 'src/app/core/api-http.client.ts';
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
    if (request === '@angular/common/http') return { HttpClient: class HttpClient {}, HttpHeaders: class HttpHeaders {}, HttpParams: class HttpParams {} };
    if (request === 'rxjs') return { firstValueFrom: value => value };
    return originalLoad.call(this, request, parent, isMain);
  };
  try {
    instance._compile(compiled, filename);
    return instance.exports.ApiHttpClient;
  } finally {
    Module._load = originalLoad;
  }
}

test('ApiHttpClient centralizes JSON requests and preserves HTTP options', async () => {
  const requests = [];
  const response = { ok: true };
  const http = {};
  for (const method of ['get', 'post', 'put', 'patch', 'delete']) {
    http[method] = (...args) => {
      requests.push({ method, args });
      return Promise.resolve(response);
    };
  }
  const ApiHttpClient = await loadClient(http);
  const api = new ApiHttpClient();
  const params = { page: '2' };
  const headers = { 'Content-Type': 'image/webp' };
  await api.get('/items', { params });
  await api.post('/items', { name: 'Bench' });
  await api.put('/items/1', { name: 'Press' }, { headers });
  await api.patch('/items/1', { name: 'Updated' });
  await api.delete('/items/1', { body: { confirm: true } });

  assert.deepEqual(requests.map(x => x.method), ['get', 'post', 'put', 'patch', 'delete']);
  assert.deepEqual(requests[0].args[1], { params });
  assert.deepEqual(requests[2].args[2], { headers });
  assert.deepEqual(requests[4].args[1], { body: { confirm: true } });
});

test('ApiHttpClient requests binary downloads without changing their response type', async () => {
  const blob = new Blob(['csv']);
  const requests = [];
  const http = { get: (...args) => { requests.push(args); return Promise.resolve(blob); } };
  const ApiHttpClient = await loadClient(http);
  const result = await new ApiHttpClient().get('/export.csv', { params: { from: '2026-11-01' }, responseType: 'blob' });

  assert.equal(result, blob);
  assert.equal(requests[0][0], '/export.csv');
  assert.deepEqual(requests[0][1], { params: { from: '2026-11-01' }, responseType: 'blob' });
});

test('ApiHttpClient propagates transport errors to the domain service', async () => {
  const failure = new Error('network failure');
  const ApiHttpClient = await loadClient({ get: () => Promise.reject(failure) });
  await assert.rejects(() => new ApiHttpClient().get('/items'), failure);
});
