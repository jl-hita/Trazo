# TRAZO backend architecture

## Initial shape

TRAZO starts as a modular monolith in one repository:

- **Trazo.Api** — ASP.NET Core HTTP boundary for Android and the admin frontend.
- **Trazo.Application** — use cases and application orchestration.
- **Trazo.Domain** — business rules and domain model.
- **Trazo.Infrastructure** — PostgreSQL persistence and external infrastructure.
- **Trazo.Admin.Web** — Angular administration frontend, built and deployed separately from the API.

The Android client lives in the separate `jl-hita/Trazo.Android` repository.

## Independence from DietoExpress

TRAZO owns its API, database, identity, commercial management, configuration and deployment. It must not depend on DietoExpress runtime services or database tables.

Algorithm improvements may be ported between repositories deliberately, with tests and review. There is no runtime or package dependency between the products.

## Database

The API uses PostgreSQL through EF Core and Npgsql. The connection string is supplied through configuration (preferably environment variables or a deployment secret), never committed with credentials.

The initial DbContext is intentionally empty. Domain entities, migrations, identity and commercial tables will be introduced as their design is agreed.

## HTTP surface

- `GET /health` — process health endpoint.
- `GET /api/v1` — API version information.
- OpenAPI document is exposed in Development only.

## Security principles

- Android and Angular are untrusted clients.
- Authorization and subscription entitlements must be enforced by the API.
- Never put secrets or privileged credentials in Angular assets or the Android APK.
- Administrative endpoints will require explicit administrator authorization.
- Logs must redact credentials, access tokens and sensitive personal data.
