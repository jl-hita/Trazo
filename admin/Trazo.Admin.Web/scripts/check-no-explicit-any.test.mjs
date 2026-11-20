import test from 'node:test';
import assert from 'node:assert/strict';
import { findExplicitAny } from './check-no-explicit-any.mjs';

test('detects explicit any in parameter, property, generic and return types', () => {
  const source = `interface Example { value: any; }\nfunction run(input: any[]): any { return input[0]; }\nconst result = Promise<any>;`;
  assert.equal(findExplicitAny(source).length, 4);
});

test('does not flag unknown, ordinary strings, or comments mentioning any', () => {
  const source = `// avoid any here\ntype Result = unknown;\nconst label = 'any';\nfunction run(value: string): unknown { return value; }`;
  assert.deepEqual(findExplicitAny(source), []);
});
