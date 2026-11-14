# TRAZO

## Strength, engineered.

**Advanced strength training**

TRAZO is an independent strength-training platform for strength and hypertrophy programming. This repository contains the ASP.NET Core backend API and the Angular administration application. The native Android client is maintained separately in [Trazo.Android](https://github.com/jl-hita/Trazo.Android).

## Repository contents

- **ASP.NET Core 10 API** — versioned REST API used by the Android client and administration frontend.
- **Domain / Application / Infrastructure** — modular backend layers with clear dependency boundaries.
- **Trazo.Training** — independent C# training-programming engine for exercise selection, program generation, progression and plan evaluation.
- **Angular administration** — operational console for user and subscription management, exercise catalog, training configuration, plan reviews, logs and system settings.
- **PostgreSQL** — TRAZO-owned database, independent from DietoExpress.
- **GitHub Actions** — backend tests, administration build and deployment workflows.

## Technology

- .NET 10 / C#
- ASP.NET Core Minimal APIs
- Entity Framework Core + Npgsql
- PostgreSQL
- Angular 21 / TypeScript
- GitHub Actions

## Local development

### Prerequisites

- .NET 10 SDK
- PostgreSQL
- Node.js and npm versions supported by the Angular workspace

Create the TRAZO database and a database login before starting the API. The configured login needs permission to create and alter schema objects because the API applies pending EF Core migrations at startup.

### Backend API

```bash
dotnet restore Trazo.sln
dotnet build Trazo.sln
dotnet test Trazo.sln
dotnet run --project src/Trazo.Api
```

Configure the database connection with the `ConnectionStrings__Trazo` environment variable. Never commit real credentials or production secrets.

Useful endpoints:

- `/health` — process liveness.
- `/health/ready` — readiness check including database connectivity.
- `GET /api/v1/exercises` — search and filter the exercise catalog.
- `GET /api/v1/exercises/{id}` — retrieve exercise details.
- `/api/v1/auth` — authentication, Google sign-in, JWT sessions and TOTP two-factor flows.

In Development, the OpenAPI document is available at `/openapi/v1.json`.

On startup, EF Core applies pending migrations. The backend also imports curated exercise additions from the versioned JSON seed; subsequent starts preserve existing catalog entries rather than overwriting them. The PostgreSQL database and login must already exist.

Further details:

- [Architecture](docs/architecture.md)
- [Authentication](docs/authentication.md)
- [Exercise catalog API](docs/exercise-catalog-api.md)
- [Exercise catalog seeding](docs/exercise-catalog-seeding.md)

### Angular administration

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

The Angular development server runs at http://localhost:4200. The administration frontend is an API-backed application with authentication and role-aware administrative features; it is not merely a static visual shell.

See [the administration README](admin/Trazo.Admin.Web/README.md) for frontend-specific instructions.

## Configuration and deployment

See [docs/configuration-and-deployment.md](docs/configuration-and-deployment.md) for the environment variables and examples covering PostgreSQL, JWT, Google authentication, TOTP, SMTP, Data Protection, systemd, Nginx/DuckDNS and the Angular application.

Deployment uses versioned releases and health checks. Application rollback restores the previous code release; it does not automatically reverse database migrations. Maintain independent database backups and verify restoration procedures before production use.

## Android client

The native Android application is maintained in [jl-hita/Trazo.Android](https://github.com/jl-hita/Trazo.Android). Its README and [configuration guide](https://github.com/jl-hita/Trazo.Android/blob/main/docs/configuration.md) describe local builds, API endpoint configuration, Google Sign-In and release setup.

## Project status

The backend, training engine, administration frontend and Android client are under active development. CI and deployment workflows are in place, but a successful build or deployment does not replace end-to-end validation, security review, tested database recovery and operational readiness checks before opening the service to the public.
