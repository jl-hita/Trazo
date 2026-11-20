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
  for (const method of ['get', 'post', 'put', 'delete']) {
    http[method] = (url, bodyOrOptions, options) => {
      requests.push({ method: method.toUpperCase(), url, argument: bodyOrOptions, options });
      return Promise.resolve(response);
    };
  }
  return { http, requests };
}

const exercisePath = 'src/app/features/exercises/exercises.api.ts';
const planningPath = 'src/app/features/planning/planning.api.ts';

test('ExercisesApi applies catalog filters and encodes exercise identifiers in resource routes', async () => {
  const { http, requests } = createHttp({ items: [] });
  const { ExercisesApi } = await loadService(exercisePath, http);
  const api = new ExercisesApi();
  await api.list({ q: ' press ', movementPattern: 'horizontal_push', status: 'active', source: 'local', sort: 'priority', page: 2, pageSize: 40 });
  await api.get('bench/press');

  assert.equal(requests[0].url, '/api/v1/admin/exercises');
  assert.deepEqual(requests[0].argument.params.toObject(), {
    page: '2', pageSize: '40', q: 'press', movementPattern: 'horizontal_push', status: 'active', source: 'local', sort: 'priority'
  });
  assert.equal(requests[1].url, '/api/v1/admin/exercises/bench%2Fpress');
});

test('ExercisesApi uploads images with the file MIME type and supports deletion', async () => {
  const { http, requests } = createHttp({ id: 'bench' });
  const { ExercisesApi } = await loadService(exercisePath, http);
  const api = new ExercisesApi();
  const file = { type: 'image/webp' };
  await api.uploadImage('bench', 2, file);
  await api.deleteImage('bench', 2);

  assert.equal(requests[0].method, 'PUT');
  assert.equal(requests[0].url, '/api/v1/admin/exercises/bench/images/2');
  assert.equal(requests[0].argument, file);
  assert.deepEqual(requests[0].options, { headers: { 'Content-Type': 'image/webp' } });
  assert.equal(requests[1].method, 'DELETE');
  assert.equal(requests[1].url, '/api/v1/admin/exercises/bench/images/2');
});

test('ExercisesApi transfers the curated library through the dedicated endpoint', async () => {
  const { http, requests } = createHttp({ sent: true });
  const { ExercisesApi } = await loadService(exercisePath, http);
  await new ExercisesApi().transferToDietoExpress();

  assert.equal(requests[0].method, 'POST');
  assert.equal(requests[0].url, '/api/v1/admin/exercise-library-transfer/dietoexpress');
  assert.deepEqual(requests[0].argument, {});
});

test('PlanningApi sends preview input and reads catalog readiness from planning routes', async () => {
  const { http, requests } = createHttp({ usableExercises: 100 });
  const { PlanningApi } = await loadService(planningPath, http);
  const api = new PlanningApi();
  const request = { goal: 'Hypertrophy', experience: 'Intermediate', durationWeeks: 4 };
  await api.preview(request);
  await api.catalogReadiness();

  assert.equal(requests[0].method, 'POST');
  assert.equal(requests[0].url, '/api/v1/admin/planning/preview');
  assert.deepEqual(requests[0].argument, request);
  assert.equal(requests[1].method, 'GET');
  assert.equal(requests[1].url, '/api/v1/admin/planning/catalog-readiness');
});
