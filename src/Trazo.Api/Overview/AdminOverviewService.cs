using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Domain.Billing;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Logging;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Overview;

/// <summary>Holds the last overview so that opening the page repeatedly does not repeat the queries.</summary>
public sealed class OverviewCache
{
    private readonly object _lock = new();
    private AdminOverview? _current;

    public AdminOverview? Current { get { lock (_lock) return _current; } set { lock (_lock) _current = value; } }
}

/// <summary>
/// Builds the overview with a handful of cheap, index-backed queries (counts and 24 h / 7 day windows, no text search
/// over the logs). The result is cached for <see cref="CacheTtl"/>; "refresh" skips the cache but never more often than
/// every <see cref="MinimumRefreshAge"/>, so a stuck button cannot hammer the database.
/// </summary>
public sealed class AdminOverviewService(
    TrazoDbContext db, AppLogReader logs, IRuntimeSettings settings, OverviewCache cache, TimeProvider clock,
    IHostEnvironment environment)
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan MinimumRefreshAge = TimeSpan.FromSeconds(10);
    public const int ErrorBurstThreshold = 5;
    public const long SlowDatabaseMs = 1000;
    private const string ErrorsLink = "/logs?levels=error&range=24h";

    public async Task<AdminOverview> GetAsync(bool refresh, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (cache.Current is { } cached)
        {
            var age = now - cached.GeneratedAt;
            if (age >= TimeSpan.Zero && age < CacheTtl && (!refresh || age < MinimumRefreshAge)) return cached;
        }
        var fresh = await BuildAsync(now, ct);
        cache.Current = fresh;
        return fresh;
    }

    /// <summary>Version and environment of this deployment. No database access.</summary>
    public OverviewSystem GetSystem()
    {
        var (version, commit) = ReadVersion();
        return new OverviewSystem(version, commit, environment.EnvironmentName);
    }

    private async Task<AdminOverview> BuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var health = await BuildHealthAsync(ct);

        var since24 = now.AddHours(-24);
        var last24 = await logs.CountBetweenAsync(since24, now.AddMinutes(1), ct);
        var previous24 = await logs.CountBetweenAsync(now.AddHours(-48), since24, ct);
        var sources = (await logs.TopErrorCategoriesAsync(since24, 5, ct))
            .Select(x => new OverviewErrorSource(x.Category, x.Count, x.LastAt,
                $"{ErrorsLink}&q={Uri.EscapeDataString(x.Category)}")).ToList();
        var logSummary = new OverviewLogs(last24.Error, last24.Warning, previous24.Error, sources, ErrorsLink, "/logs?levels=warning&range=24h");

        var email = await settings.GetEmailAsync(ct);
        var google = await settings.GetGoogleAsync(ct);
        var play = await settings.GetPlayBillingViewAsync(ct);
        var enforced = await settings.GetSubscriptionsEnforcedAsync(ct);
        var lastSend = EmailDeliveryRecorder.Parse(
            await db.AppSettings.AsNoTracking().Where(x => x.Key == EmailDeliveryRecorder.Key).Select(x => x.Value).FirstOrDefaultAsync(ct));
        var configuration = new OverviewConfiguration(
            email.IsComplete, lastSend is null ? null : new OverviewSmtpSend(lastSend.Succeeded, lastSend.At, lastSend.Error),
            !string.IsNullOrWhiteSpace(google.ClientId), play.Configured, play.RtdnConfigured, enforced,
            MissingSettings(email, google, play, enforced));

        var summary = await BuildSummaryAsync(now, enforced, ct);
        return new AdminOverview(now, (int)CacheTtl.TotalSeconds, health, logSummary, configuration, summary,
            BuildAlerts(health, logSummary, configuration, summary));
    }

    private async Task<OverviewHealth> BuildHealthAsync(CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try { await db.Database.ExecuteSqlRawAsync("SELECT 1", ct); }
        catch (Exception exception) when (exception is not OperationCanceledException) { throw new OverviewUnavailableException(exception); }
        watch.Stop();

        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var pending = all.Where(m => !applied.Contains(m)).Select(m => m[(m.IndexOf('_') + 1)..]).ToList();
        var (version, commit) = ReadVersion();
        return new OverviewHealth(true, watch.ElapsedMilliseconds, version, commit, environment.EnvironmentName,
            all.Count - pending.Count, all.Count, pending);
    }

    private async Task<OverviewSummary> BuildSummaryAsync(DateTimeOffset now, bool enforced, CancellationToken ct)
    {
        var since7 = now.AddDays(-7);
        var usersTotal = await db.Users.AsNoTracking().CountAsync(ct);
        var usersNew = await db.Users.AsNoTracking().CountAsync(x => x.CreatedAt >= since7, ct);
        var plans = await db.TrainingPlans.AsNoTracking().CountAsync(x => x.CreatedAt >= since7, ct);
        var workouts = await db.WorkoutLogs.AsNoTracking().CountAsync(x => x.LoggedAt >= since7, ct);

        var byStatus = (await db.Subscriptions.AsNoTracking().GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Status, x => x.Count);
        var statuses = Enum.GetValues<SubscriptionStatus>()
            .Select(s => new OverviewCount(SubscriptionService.ToApiName(s), byStatus.GetValueOrDefault(s))).ToList();

        // Same rules as the planning engine: only active exercises count, and the mapper decides which are usable.
        var active = await db.ExerciseCatalog.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var mapping = ExerciseCandidateMapper.Map(active);
        var withoutImages = (await db.Database.SqlQueryRaw<int>(
            """
            SELECT CAST(COUNT(*) AS INTEGER) AS "Value" FROM exercise_catalog c
            WHERE c."IsActive" AND NOT EXISTS (SELECT 1 FROM exercise_catalog_images i WHERE i.exercise_id = c.exercise_id)
            """).ToListAsync(ct)).FirstOrDefault();

        return new OverviewSummary(usersTotal, usersNew, plans, workouts, statuses.Sum(x => x.Count), statuses, enforced,
            active.Count, withoutImages, mapping.Skipped.Count, mapping.Candidates.Count);
    }

    private static IReadOnlyList<OverviewMissingSetting> MissingSettings(
        EmailConfig email, GoogleConfig google, PlayBillingView play, bool enforced)
    {
        var missing = new List<OverviewMissingSetting>();
        void Add(string id, string label) => missing.Add(new OverviewMissingSetting(id, label, "/settings"));
        if (string.IsNullOrWhiteSpace(google.ClientId)) Add("google-client-id", "ID de cliente de Google");
        if (string.IsNullOrWhiteSpace(email.Host)) Add("smtp-host", "Servidor SMTP");
        if (string.IsNullOrWhiteSpace(email.From)) Add("email-from", "Remitente de los correos");
        if (string.IsNullOrWhiteSpace(email.ActionBaseUrl)) Add("email-action-base-url", "URL base de los enlaces de los correos");
        // Play Billing only blocks anything once subscriptions are required, so it is "missing" only then.
        if (enforced && !play.Configured) Add("play-billing", "Google Play Billing (paquete y cuenta de servicio)");
        if (enforced && play.Configured && !play.RtdnConfigured) Add("play-rtdn", "Notificaciones de Google Play (RTDN)");
        return missing;
    }

    public static IReadOnlyList<OverviewAlert> BuildAlerts(
        OverviewHealth health, OverviewLogs logs, OverviewConfiguration config, OverviewSummary summary)
    {
        var alerts = new List<OverviewAlert>();
        void Add(string id, string severity, string title, string detail, string link) =>
            alerts.Add(new OverviewAlert(id, severity, title, detail, link));

        if (health.PendingMigrations.Count > 0)
            Add("migrations-pending", "critical", "Migraciones de base de datos pendientes",
                $"Hay {health.PendingMigrations.Count} migración(es) sin aplicar: {string.Join(", ", health.PendingMigrations.Take(3))}. " +
                "Reinicia la API para aplicarlas o revisa el despliegue.", "/");
        if (health.DatabaseLatencyMs >= SlowDatabaseMs)
            Add("database-slow", "warning", "La base de datos responde despacio",
                $"Una consulta mínima ha tardado {health.DatabaseLatencyMs} ms.", "/");

        if (!config.SmtpConfigured)
            Add("smtp-not-configured", "warning", "El correo SMTP no está configurado",
                "Sin correo no se pueden verificar cuentas ni recuperar contraseñas. Completa servidor, remitente y URL base.", "/settings");
        else if (config.LastSmtpSend is { Succeeded: false } failed)
            Add("smtp-failing", "critical", "El último envío de correo ha fallado",
                $"Error {failed.Error ?? "desconocido"}. Revisa la configuración SMTP y envía un correo de prueba.", "/settings");
        if (!config.GoogleConfigured)
            Add("google-not-configured", "warning", "El inicio de sesión con Google no está configurado",
                "Falta el ID de cliente de Google, por lo que los usuarios no pueden entrar con Google.", "/settings");

        if (config.SubscriptionsEnforced && !config.PlayBillingConfigured)
            Add("play-not-configured", "critical", "Suscripciones obligatorias sin Google Play Billing",
                "Se exigen suscripciones pero Google Play Billing no está configurado: nadie puede suscribirse.", "/settings");
        else if (config.PlayBillingConfigured && !config.PlayRtdnConfigured)
            Add("play-rtdn-missing", "warning", "Google Play Billing sin notificaciones (RTDN)",
                "Sin RTDN las renovaciones, cancelaciones y reembolsos no se reflejan hasta que se refresque cada suscripción.", "/settings");

        if (logs.Errors24h >= ErrorBurstThreshold && logs.Errors24h >= 2 * Math.Max(logs.ErrorsPrevious24h, 1))
            Add("errors-rising", "warning", "Los errores están aumentando",
                $"{logs.Errors24h} errores en las últimas 24 h frente a {logs.ErrorsPrevious24h} en las 24 h anteriores.", logs.ErrorsLink);
        else if (logs.Errors24h > 0)
            Add("errors-present", "info", $"{logs.Errors24h} error(es) en las últimas 24 h",
                "Revisa los registros para ver si necesitan atención.", logs.ErrorsLink);

        if (summary.ExercisesUsable == 0)
            Add("catalog-empty", "critical", "El motor no tiene ejercicios utilizables",
                "Ningún ejercicio activo es utilizable por el motor de planificación: no se pueden generar planes.", "/planning");
        else if (summary.ExercisesUnusable > 0)
            Add("catalog-unusable", "info", $"{summary.ExercisesUnusable} ejercicio(s) activos no utilizables por el motor",
                "Faltan datos (patrón de movimiento, nivel u objetivos) para que el motor los use.", "/planning");
        if (summary.ExercisesWithoutImages > 0)
            Add("catalog-no-images", "info", $"{summary.ExercisesWithoutImages} ejercicio(s) activos sin imágenes",
                "Los usuarios verán estos ejercicios sin ilustración.", "/exercises");

        return alerts.OrderBy(a => a.Severity switch { "critical" => 0, "warning" => 1, _ => 2 }).ToList();
    }

    private static (string Version, string? Commit) ReadVersion()
    {
        var raw = typeof(AdminOverviewService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? typeof(AdminOverviewService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var plus = raw.IndexOf('+');
        if (plus < 0) return (raw, null);
        var commit = raw[(plus + 1)..];
        return (raw[..plus], commit.Length > 7 ? commit[..7] : commit);
    }
}
