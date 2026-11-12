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

## Database and first startup

The API uses PostgreSQL through EF Core and Npgsql. The connection string is supplied through configuration (preferably environment variables or a deployment secret), never committed with credentials.

At startup, before accepting HTTP requests, the API calls EF Core `MigrateAsync()`. This applies all migrations not yet recorded in EF Core's migration history. The same mechanism handles a brand-new schema and later application upgrades.

Important operational details:

- Provision a dedicated TRAZO PostgreSQL database and login once, or grant the deployment login the required database-creation privileges if the database itself should be created automatically.
- Supply the connection string as `ConnectionStrings__Trazo`.
- The PostgreSQL role must have sufficient privileges to create/alter objects in the TRAZO database.
- Every schema change must be represented by a reviewed EF Core migration committed to this repository.
- Do not use `EnsureCreated()`: it bypasses migrations and is unsuitable for an evolving production schema.
- Back up the database before deploying schema-changing releases.
- For multiple API instances, run migrations as a single deployment step or use a deployment lock; do not let several instances race to migrate concurrently.

The initial DbContext is intentionally empty, so there are no application tables until the first domain migrations are added. The automatic migration mechanism is already wired.

## Health endpoints

- `GET /health` — process liveness; does not require the database.
- `GET /health/ready` — readiness; verifies that PostgreSQL is reachable.
- `GET /api/v1` — API version information.
- OpenAPI document is exposed in Development only.

## Security principles

- Android and Angular are untrusted clients.
- Authorization and subscription entitlements must be enforced by the API.
- Never put secrets or privileged credentials in Angular assets or the Android APK.
- Administrative endpoints will require explicit administrator authorization.
- Logs must redact credentials, access tokens and sensitive personal data.
