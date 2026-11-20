# Admin frontend HTTP, errors and loading policy

This document defines the conventions for the Angular administrative frontend. It complements the shared transport in `src/app/core/api-http.client.ts` and the UI error mapper in `src/app/core/api-error.ts`.

## HTTP responsibilities

- Domain API services inject `ApiHttpClient` and own endpoint paths, query parameters, request bodies and response types.
- `ApiHttpClient` owns the common `HttpClient` transport and preserves the original HTTP response/error. It must not convert failures into display strings.
- Authentication, token refresh and authorization headers remain the responsibility of `AuthService` and the HTTP interceptor.
- Direct `HttpClient` calls are reserved for isolated authentication or public one-time-token flows that do not belong to an authenticated admin domain service (login, password reset, email verification and public waitlist confirmation/unsubscribe). Do not introduce direct calls from ordinary admin feature pages.
- Do not change API routes or payloads to simplify frontend handling.

## Error handling

At the UI boundary, use `errorMessage(error, fallback?)` for administrative API failures. It maps known HTTP statuses to the shared Spanish messages in `UI_COPY.apiErrors`, avoids exposing server exception text, and accepts a page-specific fallback when the generic unexpected-error message is not suitable.

Rules:

1. API services propagate the original rejection. They do not catch an HTTP failure just to return a string, null, or a fabricated success.
2. Administrative pages and tabs convert failures to user-facing text with `errorMessage` (or the `marketingError` alias, which delegates to it).
3. Do not display `HttpErrorResponse.error.message`, exception text, stack traces or arbitrary backend payloads directly.
4. Keep narrowly scoped domain messages when the status has a specific meaning to that flow: login credentials/2FA, expired one-time tokens, field-level validation errors, or the intentionally triggered error-ticket diagnostic. These messages must remain safe and must not expose internal server details.
5. Do not move user-facing error mapping into a global interceptor. The same status can mean different things in authentication, public token flows and ordinary admin screens.

## Loading and overlapping requests

- Loading state belongs to the page or operation that owns the interaction. Set it immediately before starting the request and clear it in `finally`, including failure paths.
- Use separate flags for independent operations that may run concurrently. Do not let one request clear another operation's loading state.
- Disable only the control that would duplicate or conflict with the active operation; do not block an unrelated part of the page with a global spinner.
- For latest-wins reads (search, filter changes, date-range refreshes), use a monotonically increasing request version. Only the latest request may update data, error or loading state. Ignore stale successes and stale failures.
- For mutations, prevent duplicate submissions with the relevant operation's busy flag. Do not discard a completed mutation merely because the user has since changed a filter; refresh the relevant data after the mutation as the flow requires.

## Cancellation

The shared API client currently exposes Promise-based methods. A request-version guard prevents stale responses from changing the UI, but it does **not** cancel the underlying network request. Do not describe it as cancellation.

Do not add a generic cancellation framework for ordinary short admin reads: the version guard is sufficient to protect the UI and avoids adding complexity to every service. If a future flow genuinely needs transport cancellation (for example, a long-running export or rapidly repeated expensive query), keep that operation observable/subscription-based and unsubscribe it when superseded or explicitly cancelled. Make that decision locally rather than changing every API service.

## Tests

- Test API services with Angular `HttpTestingController`: method, route, query parameters, body, binary response type and propagated errors.
- Test `errorMessage` status mappings and ensure arbitrary backend messages are not exposed.
- Test page-level loading/error/empty states and latest-wins behavior where overlapping requests are possible.
