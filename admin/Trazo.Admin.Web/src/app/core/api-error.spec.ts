import { describe, expect, it } from 'vitest';
import { errorMessage } from './api-error';
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
