using System.Globalization;
using System.Text.Json;

namespace Trazo.Api.Support;

public sealed record BackupCopy(string Name, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record BackupLastRun(bool Ok, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Copy, string? Error);

/// <param name="Health"><c>healthy</c>, <c>warning</c>, <c>error</c> or <c>unknown</c> (nothing recorded yet).</param>
public sealed record BackupStatusView(
    bool Configured, string Health, string? Message, DateTimeOffset? CheckedAt, int MaxAgeHours, string? Remote,
    BackupLastRun? LastRun, IReadOnlyList<BackupCopy> Copies);

/// <summary>
/// Reads what the server's backup scripts leave in the status directory (see docs/backups.md): <c>backup-status.json</c>
/// (the daily check: which copies are in Drive) and <c>backup-last-run.json</c> (how the last run ended). The API never
/// talks to Drive and holds no credentials; it only reads those two small files, and an unreadable or missing one just
/// means "no information yet".
/// </summary>
public static class BackupStatusReader
{
    public const string DefaultDirectory = "/var/lib/trazo";
    public const string StatusFile = "backup-status.json";
    public const string LastRunFile = "backup-last-run.json";
    private const int DefaultMaxAgeHours = 36;
    /// <summary>The daily check has stopped running when its own record is older than this.</summary>
    private static readonly TimeSpan CheckIsStaleAfter = TimeSpan.FromHours(50);

    public static BackupStatusView Read(string? directory, DateTimeOffset now)
    {
        var dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        using var status = Load(Path.Combine(dir, StatusFile));
        using var lastRunDoc = Load(Path.Combine(dir, LastRunFile));
        if (status is null && lastRunDoc is null)
            return new BackupStatusView(false, "unknown", null, null, DefaultMaxAgeHours, null, null, []);

        var root = status?.RootElement;
        var maxAge = root is { } r && r.TryGetProperty("maxAgeHours", out var m) && m.TryGetInt32(out var hours) && hours > 0 ? hours : DefaultMaxAgeHours;
        var checkedAt = root is { } r2 ? Date(r2, "checkedAt") : null;
        var remote = root is { } r3 ? Text(r3, "remote") : null;
        var copies = root is { } r4 ? ParseCopies(r4) : [];
        var lastRun = lastRunDoc is null ? null : ParseLastRun(lastRunDoc.RootElement);

        var (health, message) = Evaluate(copies, lastRun, checkedAt, maxAge, status is not null, now);
        return new BackupStatusView(true, health, message, checkedAt, maxAge, remote, lastRun, copies);
    }

    private static (string Health, string Message) Evaluate(
        IReadOnlyList<BackupCopy> copies, BackupLastRun? lastRun, DateTimeOffset? checkedAt, int maxAge, bool hasStatus, DateTimeOffset now)
    {
        var latest = copies.Count > 0 ? copies[0] : null;
        if (lastRun is { Ok: false } && (latest is null || lastRun.FinishedAt is null || lastRun.FinishedAt > latest.CreatedAt))
            return ("error", "La última ejecución del backup ha fallado.");
        if (!hasStatus) return ("warning", "Aún no se ha comprobado qué copias hay en Drive.");
        if (latest is null) return ("error", "No hay ninguna copia en Drive.");
        if (now - latest.CreatedAt > TimeSpan.FromHours(maxAge))
            return ("error", $"La última copia tiene más de {maxAge} horas.");
        if (checkedAt is null || now - checkedAt > CheckIsStaleAfter)
            return ("warning", "La comprobación diaria de Drive no se ejecuta desde hace tiempo.");
        return ("healthy", "Hay una copia reciente en Drive.");
    }

    private static List<BackupCopy> ParseCopies(JsonElement root)
    {
        var copies = new List<BackupCopy>();
        if (!root.TryGetProperty("copies", out var array) || array.ValueKind != JsonValueKind.Array) return copies;
        foreach (var item in array.EnumerateArray())
        {
            // rclone lsjson: Name, Size, ModTime. The name carries the moment the copy was taken (UTC).
            var name = Text(item, "Name");
            if (name is null || !name.StartsWith("trazo-", StringComparison.Ordinal) || !name.EndsWith(".tar.age", StringComparison.Ordinal)) continue;
            var created = DateTimeOffset.TryParseExact(name.AsSpan(6, Math.Min(16, name.Length - 6)).ToString(), "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed : Date(item, "ModTime");
            if (created is null) continue;
            var size = item.TryGetProperty("Size", out var s) && s.TryGetInt64(out var bytes) ? Math.Max(0, bytes) : 0;
            copies.Add(new BackupCopy(name, size, created.Value));
        }
        return copies.OrderByDescending(x => x.CreatedAt).ToList();
    }

    private static BackupLastRun ParseLastRun(JsonElement root) => new(
        root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True,
        Date(root, "startedAt"), Date(root, "finishedAt"), Text(root, "copy"), Text(root, "error") is { Length: > 0 } e ? e[..Math.Min(e.Length, 500)] : null);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        Text(element, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime() : null;

    private static JsonDocument? Load(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
