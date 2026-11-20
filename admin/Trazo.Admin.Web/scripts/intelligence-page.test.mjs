import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import Module from 'node:module';
import ts from 'typescript';

const componentPath = 'src/app/features/intelligence/intelligence.page.ts';

function createSignal(initialValue) {
  let value = initialValue;
  const read = () => value;
  read.set = next => { value = next; };
  read.update = updater => { value = updater(value); };
  return read;
}

async function loadComponent(api) {
  const filename = path.resolve(process.cwd(), componentPath);
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
    if (request === '@angular/core') {
      return {
        Component: () => target => target,
        ViewEncapsulation: { None: 2 },
        inject: () => api,
        signal: createSignal
      };
    }
    if (request === './intelligence.api') return { IntelligenceApi: class IntelligenceApi {} };
    if (request === './intelligence-period-toolbar.component') return { IntelligencePeriodToolbarComponent: class IntelligencePeriodToolbarComponent {} };
    return originalLoad.call(this, request, parent, isMain);
  };

  try {
    instance._compile(compiled, filename);
    return instance.exports.IntelligencePageComponent;
  } finally {
    Module._load = originalLoad;
  }
}

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

const sample = {
  from: '2026-10-01T00:00:00Z',
  to: '2026-11-01T00:00:00Z',
  generatedAt: '2026-11-01T10:00:00Z',
  plans: { total: 12, generated: 8, manual: 4, engineVersions: ['v2'] },
  prescriptions: { sessions: 30, withOriginalSnapshot: 27, withoutOriginalSnapshot: 3 },
  workouts: { total: 20, completed: 15, partial: 3, notCompleted: 2 },
  adaptations: [{ kind: 'deload', source: 'engine', count: 2 }],
  observations: [],
  decisions: []
};

test('Intelligence page starts loading the default period and exposes a successful result', async () => {
  const pending = deferred();
  const api = { summary: () => pending.promise, export: async () => new Blob() };
  const Component = await loadComponent(api);
  const page = new Component();

  assert.equal(page.loading(), true);
  assert.match(page.from(), /^\d{4}-\d{2}-\d{2}$/);
  assert.match(page.to(), /^\d{4}-\d{2}-\d{2}$/);

  pending.resolve(sample);
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(page.loading(), false);
  assert.equal(page.data(), sample);
  assert.equal(page.loadError(), '');
  assert.equal(page.adaptationTotal(), 2);
  assert.equal(page.pct(15, 20), '75 %');
  assert.equal(page.pct(0, 0), '—');
});

test('Intelligence page rejects an invalid date range without calling the API', async () => {
  let calls = 0;
  const api = { summary: async () => { calls++; return sample; }, export: async () => new Blob() };
  const Component = await loadComponent(api);
  const page = new Component();
  await new Promise(resolve => setImmediate(resolve));
  calls = 0;

  page.setFrom('2026-11-02');
  page.setTo('2026-11-01');
  await page.load();

  assert.equal(calls, 0);
  assert.equal(page.loadError(), 'El periodo no es válido.');
});

test('Intelligence page ignores an older response when a newer request completes first', async () => {
  const first = deferred();
  const second = deferred();
  let calls = 0;
  const api = {
    summary: () => {
      calls++;
      if (calls === 1) return Promise.resolve(sample);
      return calls === 2 ? first.promise : second.promise;
    },
    export: async () => new Blob()
  };
  const Component = await loadComponent(api);
  const page = new Component();
  await new Promise(resolve => setImmediate(resolve));

  const oldRequest = page.load();
  const newRequest = page.load();
  second.resolve({ ...sample, plans: { ...sample.plans, total: 22 } });
  await newRequest;
  first.resolve({ ...sample, plans: { ...sample.plans, total: 11 } });
  await oldRequest;

  assert.equal(page.data().plans.total, 22);
  assert.equal(page.loading(), false);
});

test('Intelligence page reports API failures and allows a later successful retry', async () => {
  let shouldFail = true;
  const api = {
    summary: async () => {
      if (shouldFail) throw { error: { message: 'Servicio temporalmente no disponible' } };
      return sample;
    },
    export: async () => new Blob()
  };
  const Component = await loadComponent(api);
  const page = new Component();
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(page.loadError(), 'Servicio temporalmente no disponible');
  assert.equal(page.loading(), false);

  shouldFail = false;
  await page.load();
  assert.equal(page.loadError(), '');
  assert.equal(page.data(), sample);
  assert.equal(page.loading(), false);
});

test('Intelligence page changes the date preset and marks manual date edits as a custom range', async () => {
  const calls = [];
  const api = { summary: async (from, to) => { calls.push({ from, to }); return sample; }, export: async () => new Blob() };
  const Component = await loadComponent(api);
  const page = new Component();
  await new Promise(resolve => setImmediate(resolve));

  page.usePreset(30);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(page.preset(), 30);
  assert.equal(calls.at(-1).to, page.to());

  page.setFrom('2026-10-15');
  page.setTo('2026-10-28');
  assert.equal(page.preset(), null);
  assert.equal(page.from(), '2026-10-15');
  assert.equal(page.to(), '2026-10-28');
});

test('Intelligence page template exposes accessible loading, error, and export states', async () => {
  const template = await readFile(path.resolve(process.cwd(), 'src/app/features/intelligence/intelligence.page.html'), 'utf8');
  const toolbar = await readFile(path.resolve(process.cwd(), 'src/app/features/intelligence/intelligence-period-toolbar.component.html'), 'utf8');
  const combinedTemplate = `${template}\\n${toolbar}`;
  assert.match(template, /role="alert"/);
  assert.match(template, /role="status"/);
  assert.match(template, /Cargando datos de investigación/);
  assert.match(combinedTemplate, /aria-label="Periodo"/);
  assert.match(combinedTemplate, /\[disabled\]="exporting\(\) \|\| loading\(\)"/);
  assert.match(combinedTemplate, /Exportar investigación \(ZIP\)/);
});
