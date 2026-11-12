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

Requires the .NET 10 SDK and PostgreSQL (the API can start without a configured database connection while the foundation is being built).

```bash
dotnet restore Trazo.sln
dotnet build Trazo.sln
dotnet test Trazo.sln
dotnet run --project src/Trazo.Api
```

The API listens on the URLs shown by ASP.NET Core. In Development, the OpenAPI document is available at `/openapi/v1.json`. Health check: `/health`.

Configure PostgreSQL with the `ConnectionStrings__Trazo` environment variable. Do not commit real credentials.

### Angular administration

```bash
cd admin/Trazo.Admin.Web
npm install
npm start
```

The initial administration interface is a visual shell. It is not yet connected to live API data and does not implement authentication or administrative actions.

## Architecture

See [docs/architecture.md](docs/architecture.md).

## Project status

Early development — backend and administration foundations.
