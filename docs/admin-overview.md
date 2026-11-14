# Administration overview

The **Overview** page of the administration console shows the state of the system and the alerts that say what to do next.
It is superadmin only and shows **aggregates only**: no health data, emails, log messages or stack traces.

## API

| Route | Meaning |
|---|---|
| `GET /api/v1/admin/overview` | Everything below in one response. `?refresh=true` skips the cache |
| `GET /api/v1/admin/overview/system` | Version, commit and environment of the deployment (no database access; used by the console footer) |

`503` means the database does not answer; the page then shows an error with a retry button.

### Cache

The result is cached in memory for **90 seconds** (`cacheSeconds`, `generatedAt` in the response). "Actualizar" asks for
`refresh=true`, which rebuilds the result, but never more often than every **10 seconds**: a refresh sooner than that
returns the cached result. The cache is per API process.

### Cost

A handful of index-backed queries: counts and 24 h / 7 day windows only. Logs are read with the existing
`(level, timestamp)` index and **never** with `LIKE` over message text. Migration `OverviewIndexes` adds indexes on
`users."CreatedAt"`, `training_plans."CreatedAt"` and `workout_logs."LoggedAt"` for the 7-day counts.

## What each card measures

| Card | Field | Source |
|---|---|---|
| **Salud del sistema** | `health.databaseOk`, `databaseLatencyMs` | A `SELECT 1` timed on the request that builds the overview |
| | `health.migrationsApplied` / `migrationsTotal` / `pendingMigrations` | EF: migrations in the assembly vs `__EFMigrationsHistory` |
| | `health.version`, `commit` | `InformationalVersion` of `Trazo.Api` (see below) |
| | `health.environment` | `ASPNETCORE_ENVIRONMENT` |
| **Registros** | `logs.errors24h`, `warnings24h` | `app_logs`, last 24 h; errors = level 4 and 5, warnings = level 3 |
| | `logs.errorsPrevious24h` | Same, the 24 h before (to detect a rise) |
| | `logs.topErrorSources` | Top 5 **categories** (loggers) by errors in 24 h. Only category, count and last time; the link opens **Logs** filtered by level, last 24 h and that category |
| **Configuración** | `smtpConfigured` | Host, sender, base URL and port set (database value, else environment) |
| | `lastSmtpSend` | Result of the last SMTP send, see below |
| | `googleConfigured` | Google client ID set |
| | `playBillingConfigured` / `playRtdnConfigured` | Package + service account / plus RTDN secret or audience |
| | `missing` | Important settings not set. Play Billing is listed only when subscriptions are required |
| **Usuarios** | `usersTotal`, `usersNew7d` | `users`, by `CreatedAt` |
| **Actividad (7 días)** | `plansCreated7d`, `workoutsLogged7d` | `training_plans.CreatedAt`, `workout_logs.LoggedAt` |
| **Suscripciones** | `subscriptionsByStatus`, `subscriptionsTotal` | `subscriptions` grouped by status |
| | `subscriptionsEnforced` | The "subscriptions required" switch. When off the card says **"Suscripciones no obligatorias"** |
| **Catálogo de ejercicios** | `exercisesActive` | Active rows of `exercise_catalog` |
| | `exercisesUsable` / `exercisesUnusable` | The planning engine's own mapper (`ExerciseCandidateMapper`) over the active exercises, as in `catalog-readiness` |
| | `exercisesWithoutImages` | Active exercises with no row in `exercise_catalog_images` |

### Last SMTP send

`SmtpEmailSender` (verification and recovery emails) and the SMTP test of the settings page record the outcome in
`app_settings`, key `Email:Smtp:LastSend`, as `{"ok":…,"at":…,"error":…}`. `error` is only the **exception type** (for
example `AuthenticationException`), never the server message, address or subject. Recording uses its own database scope and
never fails or delays the send. A setup with no email sent yet shows "Configurado" without a result.

### Version

The API reads `AssemblyInformationalVersion`. `Trazo.Api.csproj` sets `SourceRevisionId` from `GITHUB_SHA`, which GitHub
Actions defines on every run, so the deploy workflow needs **no change**: the build in "Deploy TRAZO" produces
`1.0.0+<commit>` and the console shows `v1.0.0 (abc1234)`. Local builds have no commit and show the version only.

## Alerts

Each alert has a `severity` (`critical`, `warning`, `info`), a Spanish `title` and `detail`, and a `link` (an admin route,
possibly with a query string). They are sorted critical first.

| Id | Severity | When |
|---|---|---|
| `migrations-pending` | critical | The database lacks migrations the deployed code has |
| `database-slow` | warning | A `SELECT 1` took 1 s or more |
| `smtp-not-configured` | warning | Host, sender or base URL missing |
| `smtp-failing` | critical | SMTP is configured and the last send failed |
| `google-not-configured` | warning | No Google client ID |
| `play-not-configured` | critical | Subscriptions are required but Play Billing is not configured |
| `play-rtdn-missing` | warning | Play Billing configured without RTDN |
| `errors-rising` | warning | 5 or more errors in 24 h **and** at least twice the previous 24 h |
| `errors-present` | info | Any error in 24 h (when not rising) |
| `catalog-empty` | critical | No active exercise is usable by the engine |
| `catalog-unusable` | info | Some active exercises are not usable |
| `catalog-no-images` | info | Some active exercises have no images |

The rules live in `AdminOverviewService.BuildAlerts` and are unit tested.

## Logs links

The **Logs** page reads `levels` (comma-separated), `range=24h` (from yesterday's date) and `q` from the URL, which is how
the overview links to a filtered view.

## Not included yet

Funnel, trends and planning-engine quality metrics are intentionally left for later. **Backups** stays disabled in the menu
with a "No disponible" note.

## Product insights (`insights`)

A second block of the same response, below the system cards ("Uso y adopción"). It is built by `OverviewInsightsBuilder`
with plain counts, uses no new tables or migrations and shares the 90 s cache.

| Card | Field | Source |
|---|---|---|
| **Actividad de los últimos 14 días** | `activity.days[]` | Signups (`users.CreatedAt`), plans (`training_plans.CreatedAt`) and logged sessions (`workout_logs.LoggedAt`) per UTC day |
| | `activeUsers7d` / `activeUsers30d` | Users by `LastLoginAt` |
| **Embudo de activación** | `funnel` | Registered → email verified (or Google) → with a plan → with a logged session → "constantes" (3 or more sessions). Counts of users, never who |
| **Suscripciones a vigilar** | `subscriptions` | Trials ending in 7 days, cancelled subscriptions whose access ends in 7 days, on-hold or grace-period payments. Test purchases are excluded and counted apart |
| **Motor y entrenamientos (30 días)** | `engine` | Outcome of logged sessions, % with pain reported, reasons for not completing, and the automatic adjustments of the engine (`plan_adaptations` by kind) |

Extra alerts: `subscriptions-payment-issues` (warning), `trials-ending` (info) and `users-unverified` (info, accounts with no
verified email after 3 days).

### Privacy

Everything is aggregated. Health-related figures follow a minimum group size of **5**: the pain percentage is `null` ("Pocos
datos") until at least 5 sessions exist in the period, and a reason for not completing with fewer than 5 sessions is merged into
"Otros". No emails, names, notes or per-user lists appear in this block.

### Not measured yet

Revenue (prices are not stored), fine-grained retention by cohort, most-substituted exercises and the quality analyser's warnings
(neither is persisted today), and backups (the app has no backup status to read).
