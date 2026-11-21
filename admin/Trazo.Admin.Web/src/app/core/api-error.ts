import { UI_COPY } from './ui-copy';

interface ErrorResponseLike {
  status?: unknown;
  error?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

const TRACE_ID_PATTERN = /^[0-9A-Za-z:-]{8,64}$/;

/**
 * The incident reference of a failed request: the `traceId` the API puts in every error answer, shortened to what is enough to find it in
 * the server logs (the 32-hex trace of a W3C id `00-<trace>-<span>-<flags>`, or the whole id when it has another shape).
 * Null when the answer carries none (connection failures, answers of a proxy).
 */
export function errorReference(error: unknown): string | null {
  const body = isRecord(error) ? (error as ErrorResponseLike).error : null;
  const traceId = isRecord(body) ? body['traceId'] : null;
  if (typeof traceId !== 'string' || !TRACE_ID_PATTERN.test(traceId)) return null;
  const parts = traceId.split('-');
  const id = parts.length >= 3 && parts[1].length === 32 ? parts[1] : traceId;
  return id.slice(0, 12);
}

function withReference(message: string, error: unknown): string {
  const reference = errorReference(error);
  return reference ? `${message} (${UI_COPY.apiErrors.reference} ${reference})` : message;
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
export function errorMessage(error: unknown, fallback: string = UI_COPY.apiErrors.unexpected): string {
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
  // Server-side failures quote the incident reference, so the person who reports it can be matched with the ticket and the logs.
  if (status === 502 || status === 503 || status === 504) return withReference(UI_COPY.apiErrors.unavailable, error);
  if (status !== null && status >= 500) return withReference(UI_COPY.apiErrors.server, error);

  return fallback;
}
