import { UI_COPY } from './ui-copy';

interface ErrorResponseLike {
  status?: unknown;
  error?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function hasValidationErrors(body: unknown): boolean {
  if (!isRecord(body) || !isRecord(body['errors'])) return false;
  return Object.keys(body['errors']).length > 0;
}

/**
 * Converts transport failures into consistent, user-facing Spanish messages.
 * The original HttpErrorResponse is deliberately left untouched by ApiHttpClient,
 * so callers can still inspect status and headers when a flow needs them.
 */
export function errorMessage(error: unknown, fallback = UI_COPY.apiErrors.unexpected): string {
  const response = isRecord(error) ? error as ErrorResponseLike : null;
  const status = typeof response?.status === 'number' ? response.status : null;
  const body = response?.error;

  if (status === 0 || status === 408) return UI_COPY.apiErrors.network;
  if (status === 400) return hasValidationErrors(body) ? UI_COPY.apiErrors.validation : UI_COPY.apiErrors.badRequest;
  if (status === 401) return UI_COPY.apiErrors.unauthorized;
  if (status === 403) return UI_COPY.apiErrors.forbidden;
  if (status === 404) return UI_COPY.apiErrors.notFound;
  if (status === 409) return UI_COPY.apiErrors.conflict;
  if (status === 413) return UI_COPY.apiErrors.tooLarge;
  if (status === 422) return UI_COPY.apiErrors.validation;
  if (status === 429) return UI_COPY.apiErrors.rateLimited;
  if (status === 502 || status === 503 || status === 504) return UI_COPY.apiErrors.unavailable;
  if (status !== null && status >= 500) return UI_COPY.apiErrors.server;

  return fallback;
}
