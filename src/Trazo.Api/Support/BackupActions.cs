using System.Text.RegularExpressions;

namespace Trazo.Api.Support;

public sealed record BackupActionResult(bool Ok, int StatusCode, string Message);

/// <summary>
/// What the backup panel can ask the server to do (see docs/backups.md). The API has no Drive access and no privileges, so it only
/// drops small request files in a directory that systemd watches (<c>trazo-backup-now.path</c>, <c>trazo-restore-request.path</c>);
/// root-owned scripts do the work. The age private key a restore needs is written to that same directory (a tmpfs, owner-only,
/// removed by the script as soon as it picks the request up) and is never logged or stored in the database.
/// </summary>
public sealed class BackupActions(IConfiguration configuration, TimeProvider time)
{
    public const string DefaultRequestDirectory = "/run/trazo";
    public const string BackupNowFile = "backup-now";
    public const string RestoreRequestFile = "restore-request";
    public const string RestoreKeyFile = "restore-key";
    public const string ProductionConfirmation = "RESTAURAR";
    private static readonly Regex CopyName = new(@"^trazo-[0-9]{8}T[0-9]{6}Z\.tar\.age$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyLine = new(@"^AGE-SECRET-KEY-1[A-Z0-9]{20,150}$", RegexOptions.CultureInvariant);

    private string StatusDirectory => configuration["Backups:StatusPath"] is { Length: > 0 } s ? s : BackupStatusReader.DefaultDirectory;
    private string RequestDirectory => configuration["Backups:RequestPath"] is { Length: > 0 } r ? r : DefaultRequestDirectory;

    public BackupActionResult RequestBackup()
    {
        if (!Directory.Exists(RequestDirectory)) return Unavailable();
        if (Busy() is { } busy) return busy;
        try { WriteAtomically(BackupNowFile, string.Empty); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Unavailable(); }
        return new(true, StatusCodes.Status202Accepted, "Copia en marcha: aparecerá en la lista cuando termine de subirse a Drive.");
    }

    public BackupActionResult RequestRestore(string? copy, string? mode, string? key, string? confirmation)
    {
        if (copy is null || !CopyName.IsMatch(copy)) return Invalid("Elige una copia de la lista.");
        if (mode is not ("test" or "production")) return Invalid("Elige verificar en la base de prueba o restaurar en producción.");
        if (mode == "production" && confirmation != ProductionConfirmation)
            return Invalid($"Para restaurar en producción escribe {ProductionConfirmation} para confirmar.");
        var keyLines = (key ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#')).ToList();
        if (keyLines.Count != 1 || !KeyLine.IsMatch(keyLines[0]))
            return Invalid("Pega la clave privada completa (la línea que empieza por AGE-SECRET-KEY-1).");
        if (!BackupStatusReader.Read(StatusDirectory, time.GetUtcNow()).Copies.Any(c => c.Name == copy))
            return Invalid("Esa copia ya no está en la lista de Drive. Actualiza la página.");
        if (!Directory.Exists(RequestDirectory)) return Unavailable();
        if (Busy() is { } busy) return busy;
        try
        {
            WriteAtomically(RestoreKeyFile, keyLines[0] + "\n");
            WriteAtomically(RestoreRequestFile, $"copy={copy}\nmode={mode}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Unavailable(); }
        return new(true, StatusCodes.Status202Accepted, mode == "production"
            ? "Restauración en marcha. El servidor se parará unos minutos."
            : "Verificación en marcha en la base de prueba; no se toca producción.");
    }

    private BackupActionResult? Busy()
    {
        var now = time.GetUtcNow();
        if (BackupStatusReader.ReadRestore(StatusDirectory) is { State: "running" })
            return new(false, StatusCodes.Status409Conflict, "Hay una restauración en curso.");
        if (BackupStatusReader.IsRunning(StatusDirectory, now))
            return new(false, StatusCodes.Status409Conflict, "Hay una copia en curso. Espera a que termine.");
        if (File.Exists(Path.Combine(RequestDirectory, BackupNowFile)) || File.Exists(Path.Combine(RequestDirectory, RestoreRequestFile)))
            return new(false, StatusCodes.Status409Conflict, "Ya hay una petición pendiente. Espera unos segundos.");
        return null;
    }

    private static BackupActionResult Invalid(string message) => new(false, StatusCodes.Status400BadRequest, message);

    private static BackupActionResult Unavailable() => new(false, StatusCodes.Status503ServiceUnavailable,
        "Este servidor no admite acciones desde el panel: faltan las unidades de systemd (reinstala con deploy/scripts/install-trazo-backup.sh).");

    /// <summary>Owner-only, written under a temporary name and renamed so a watcher never sees a half-written file.</summary>
    private void WriteAtomically(string name, string content)
    {
        var target = Path.Combine(RequestDirectory, name);
        var temp = Path.Combine(RequestDirectory, $".{name}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temp, options))
            using (var writer = new StreamWriter(stream)) writer.Write(content);
            File.Move(temp, target, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
    }
}
