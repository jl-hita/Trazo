# TRAZO

**Strength training, built around the lifter.**

TRAZO is a strength-training platform focused on structured programming, measurable progress and training plans that respond to real workout data. It brings together a native Android app, an ASP.NET Core API and an Angular administration console.

The Android client lives in [jl-hita/Trazo.Android](https://github.com/jl-hita/Trazo.Android).

## The product

TRAZO is designed around a simple idea: a training plan should be more than a static list of exercises. It should provide a clear prescription, record what actually happened and use that information to guide the next sessions.

The platform currently includes:

- Personalised training plans built from the user's goal, experience, schedule, available equipment and declared limitations.
- Macrocycle and mesocycle structure, with training blocks generated as they become relevant.
- Session prescriptions covering exercises, sets, rep ranges, RIR, rest and load targets.
- Workout logging and training history.
- Progression and adaptation based on completed work, performance, recovery and reported discomfort.
- Exercise alternatives that respect the plan's objective, equipment and movement pattern.
- Account management, notifications and a separate administration console.

## How it is built

The backend is a modular monolith. Business rules and the training engine are kept separate from HTTP, persistence and external services.

```text
Trazo.Android        Native Android application (Kotlin / Compose)
        |
        | HTTPS · versioned REST API
        v
Trazo.Api            ASP.NET Core API
        |
        +-- Trazo.Application   Use cases and orchestration
        +-- Trazo.Domain        Domain model and business rules
        +-- Trazo.Infrastructure
        |                       PostgreSQL, EF Core and integrations
        +-- Trazo.Training      Standalone C# training engine

Trazo.Admin.Web      Angular administration application
```

The API is the source of truth for identity, permissions, plans, progression and subscription entitlements. The Android client presents the results and records training activity; it does not make its own decisions about access or plan progression.

The project has its own database, authentication, configuration and deployment. It does not depend on DietoExpress at runtime.

## Training engine

The `Trazo.Training` project contains deterministic training logic without dependencies on ASP.NET Core, Entity Framework or the database. The application layer supplies the user's profile, exercise catalogue and training history, then coordinates the resulting prescriptions.

Among the implemented capabilities:

- Exercise selection and session composition.
- Weekly volume allocation and exercise ordering.
- Macrocycle phases and mesocycle generation.
- Double progression and working-load estimates.
- Session-level adaptation from effort, completion, recovery and pain feedback.
- Exercise substitutions and prescription quality analysis.

Plans are generated in blocks rather than exposing an entire future programme at once. Logged sessions feed the progression and adaptation logic. Changes are bounded and recorded so that the plan's history remains explainable.

More detail: [Planning engine](docs/planning-engine.md).

## Administration

The Angular console is an operational application connected to the same API. It includes:

- User and account administration.
- Exercise catalogue management and review.
- Planning previews and catalogue-readiness diagnostics.
- System overview, usage metrics and application logs.
- Error-ticket investigation for backend exceptions and Android crashes.
- Configuration for authentication, email, notifications and Google Play.
- Subscription and promotion management.

Unexpected backend exceptions and reported Android crashes are collected as grouped error tickets. Android release builds upload their R8 mapping files so supported crash traces can be de-obfuscated before investigation.

## Technology

| Area | Stack |
| --- | --- |
| Backend | .NET 10, C#, ASP.NET Core Minimal APIs |
| Application architecture | Domain / Application / Infrastructure |
| Training engine | Standalone C# library |
| Persistence | PostgreSQL, Entity Framework Core, Npgsql |
| Administration | Angular 21, TypeScript |
| Android | Kotlin, Jetpack Compose |
| Authentication | JWT, Google Sign-In, TOTP |
| Notifications | Firebase Cloud Messaging |
| Delivery | GitHub Actions, versioned deployments |

## Repository layout

```text
src/
  Trazo.Api/
  Trazo.Application/
  Trazo.Domain/
  Trazo.Infrastructure/
  Trazo.Training/
admin/
  Trazo.Admin.Web/
tests/
  Trazo.UnitTests/
docs/
```

The Android application is maintained separately: [Trazo.Android](https://github.com/jl-hita/Trazo.Android).

## Run locally

### Requirements

- .NET 10 SDK
- PostgreSQL
- Node.js and npm compatible with the Angular workspace

### API

Create a dedicated PostgreSQL database and configure the connection string through an environment variable:

```bash
export ConnectionStrings__Trazo="Host=localhost;Database=trazo;Username=trazo;Password=your-password"
dotnet restore Trazo.sln
dotnet build Trazo.sln
dotnet test Trazo.sln
dotnet run --project src/Trazo.Api
```

The API applies pending EF Core migrations at startup. A database login with permission to create and alter schema objects is required.

Useful endpoints:

- `GET /health` — process liveness.
- `GET /health/ready` — readiness, including database connectivity.
- `GET /api/v1/exercises` — exercise catalogue.
- `/api/v1/plans` — training plans and sessions.
- `/openapi/v1.json` — OpenAPI document in Development.

### Administration

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

The Angular development server runs at `http://localhost:4200`. Configure its API target for the local backend as described in the frontend documentation.

## Engineering practices

- Backend unit tests and a separate Angular build run in GitHub Actions.
- API changes are covered by application and endpoint tests where appropriate.
- Database changes are delivered as EF Core migrations.
- API errors use a stable problem-response contract; unexpected failures are captured for diagnosis rather than exposed to clients.
- Deployment uses versioned releases and health checks. Database backups and migration planning remain separate operational responsibilities.

## Documentation

- [Architecture](docs/architecture.md)
- [Planning engine](docs/planning-engine.md)
- [Priority roadmap](docs/priority-roadmap.md)
- [Authentication](docs/authentication.md)
- [Configuration and deployment](docs/configuration-and-deployment.md)
- [Error tickets](docs/error-tickets.md)
- [Subscriptions](docs/subscriptions.md)
- [Push notifications](docs/push-notifications.md)

## Project status

TRAZO is an actively developed product, not a finished public service. The core Android, API and administration flows are implemented and continue to evolve. CI and deployment automation are in place; production readiness still depends on continued end-to-end validation, security review and operational testing.
