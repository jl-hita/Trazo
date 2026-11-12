# TRAZO

## Strength, engineered.

**ADVANCED STRENGTH TRAINING**

TRAZO is an independent strength-training platform for experienced lifters. This repository contains the backend API and the Angular administration frontend. The Android application is maintained separately in [Trazo.Android](https://github.com/jl-hita/Trazo.Android).

## Repository contents

- **ASP.NET Core 10 API** — REST API for the Android app and administration frontend.
- **Domain / Application / Infrastructure** — modular backend layers.
- **Angular administration** — operational console for users, subscriptions, training configuration, logs, backups and system settings.
- **PostgreSQL** — TRAZO-owned database, independent from DietoExpress.
- **GitHub Actions** — separate backend and admin frontend CI workflows.

## Technology

- .NET 10 / C#
- ASP.NET Core
- Entity Framework Core + Npgsql
- PostgreSQL
- Angular 21 / TypeScript
- GitHub Actions

## Local development

### Backend API

Requires the .NET 10 SDK and a PostgreSQL database/login. On startup, the API automatically applies pending EF Core migrations before it begins accepting requests.

```bash
dotnet restore Trazo.sln
dotnet build Trazo.sln
dotnet test Trazo.sln
dotnet run --project src/Trazo.Api
```

Configure PostgreSQL with the `ConnectionStrings__Trazo` environment variable. Do not commit real credentials. The configured role needs permission to create and alter schema objects in the dedicated TRAZO database.

- `/health` checks that the API process is alive.
- `/health/ready` checks database connectivity.
- `GET /api/v1/exercises` searches and filters the exercise catalog; `GET /api/v1/exercises/{id}` returns full exercise details. See [docs/exercise-catalog-api.md](docs/exercise-catalog-api.md).
- `/api/v1/auth` provides password and Google sign-in, JWT sessions, TOTP two-factor authentication and recovery codes. See [docs/authentication.md](docs/authentication.md).
- In Development, OpenAPI is available at `/openapi/v1.json`.

On the first API start, EF Core applies pending migrations and the backend imports the curated exercise additions from the versioned JSON seed. Later starts are idempotent: existing catalog entries are preserved and are not overwritten by the seed. The PostgreSQL database and login must exist beforehand; the API creates and upgrades its tables automatically. See [docs/architecture.md](docs/architecture.md) and [docs/exercise-catalog-seeding.md](docs/exercise-catalog-seeding.md) for details.

### Angular administration

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

The initial administration interface is a visual shell. It is not yet connected to live API data and does not implement authentication or administrative actions.

## Configuration and deployment

See [docs/configuration-and-deployment.md](docs/configuration-and-deployment.md) for the complete API environment file, PostgreSQL, JWT/Google/2FA, SMTP, Data Protection, systemd, Nginx/DuckDNS and Angular administration setup.

## Architecture

See [docs/architecture.md](docs/architecture.md).

## Project status

Early development — backend and administration foundations.
