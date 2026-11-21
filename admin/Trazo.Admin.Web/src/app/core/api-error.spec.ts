import { describe, expect, it } from 'vitest';
import { errorMessage, errorReference } from './api-error';
import { UI_COPY } from './ui-copy';

describe('errorMessage', () => {
  it('normalizes connection failures and timeouts', () => {
    expect(errorMessage({ status: 0 })).toBe(UI_COPY.apiErrors.network);
    expect(errorMessage({ status: 408 })).toBe(UI_COPY.apiErrors.network);
  });

  it('normalizes authentication and authorization failures', () => {
    expect(errorMessage({ status: 401 })).toBe(UI_COPY.apiErrors.unauthorized);
    expect(errorMessage({ status: 403 })).toBe(UI_COPY.apiErrors.forbidden);
  });

  it('normalizes validation and conflict responses without exposing server text', () => {
    expect(errorMessage({ status: 400, error: { errors: { Email: ['Invalid email'] } } }))
      .toBe(UI_COPY.apiErrors.validation);
    expect(errorMessage({ status: 400, error: { message: 'Internal details' } }))
      .toBe(UI_COPY.apiErrors.badRequest);
    expect(errorMessage({ status: 409 })).toBe(UI_COPY.apiErrors.conflict);
    expect(errorMessage({ status: 422 })).toBe(UI_COPY.apiErrors.validation);
  });

  it('normalizes missing resources, rate limits and server failures', () => {
    expect(errorMessage({ status: 404 })).toBe(UI_COPY.apiErrors.notFound);
    expect(errorMessage({ status: 413 })).toBe(UI_COPY.apiErrors.tooLarge);
    expect(errorMessage({ status: 429 })).toBe(UI_COPY.apiErrors.rateLimited);
    expect(errorMessage({ status: 502 })).toBe(UI_COPY.apiErrors.unavailable);
    expect(errorMessage({ status: 503 })).toBe(UI_COPY.apiErrors.unavailable);
    expect(errorMessage({ status: 504 })).toBe(UI_COPY.apiErrors.unavailable);
    expect(errorMessage({ status: 500 })).toBe(UI_COPY.apiErrors.server);
  });

  it('uses the caller fallback for unknown failures', () => {
    expect(errorMessage(new Error('Internal exception'), 'No se pudo guardar.')).toBe('No se pudo guardar.');
    expect(errorMessage({ status: 418 }, 'No se pudo guardar.')).toBe('No se pudo guardar.');
  });
});

describe('errorReference', () => {
  const trace = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01';

  it('quotes the incident reference of server-side failures', () => {
    const body = { type: 'about:blank', title: 'server_error', status: 500, code: 'server_error', traceId: trace };
    expect(errorReference({ status: 500, error: body })).toBe('4bf92f3577b3');
    expect(errorMessage({ status: 500, error: body })).toBe(`${UI_COPY.apiErrors.server} (Ref. 4bf92f3577b3)`);
    expect(errorMessage({ status: 503, error: { ...body, status: 503, traceId: trace } }))
      .toBe(`${UI_COPY.apiErrors.unavailable} (Ref. 4bf92f3577b3)`);
  });

  it('accepts the plain request id of a host without tracing', () => {
    expect(errorReference({ error: { traceId: '0HN4F5K2P0M1Q:00000001' } })).toBe('0HN4F5K2P0M1');
  });

  it('ignores a missing or malformed reference and never adds it to client errors', () => {
    expect(errorReference({ status: 500 })).toBeNull();
    expect(errorReference({ error: { traceId: '<script>alert(1)</script>' } })).toBeNull();
    expect(errorReference({ error: { traceId: 42 } })).toBeNull();
    expect(errorMessage({ status: 500, error: { traceId: 'x y' } })).toBe(UI_COPY.apiErrors.server);
    expect(errorMessage({ status: 404, error: { traceId: trace } })).toBe(UI_COPY.apiErrors.notFound);
  });
});
