using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Trazo.Infrastructure.Support;

namespace Trazo.Api.Support;

/// <summary>
/// Private per-release R8 mappings and the R8 Retrace invocation. A missing/invalid version, missing mapping,
/// unavailable executable or failed Retrace deliberately returns null so callers keep the original obfuscated trace.
/// </summary>
public sealed class R8MappingStore(IConfiguration configuration, ILogger<R8MappingStore> logger)
{
    public const long MaxMappingBytes = 50L * 1024 * 1024;
    private string? MappingDirectory => configuration["Diagnostics:MappingsPath"] is { Length: > 0 } path ? path : null;
    private string RetraceExecutable => configuration["Diagnostics:RetraceExecutable"] ?? "retrace";

    public async Task<bool> StoreAsync(string version, Stream content, CancellationToken ct)
    {
        var cleanVersion = ErrorTicketService.CleanVersion(version);
        var directory = MappingDirectory;
        if (cleanVersion is null || directory is null) return false;

        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, cleanVersion + ".mapping.txt");
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                if (output.Length is 0 or > MaxMappingBytes)
                    throw new InvalidDataException("The R8 mapping file is empty or exceeds the size limit.");
            }
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException ex) { logger.LogWarning("Could not remove a temporary R8 mapping file ({ExceptionType}).", ex.GetType().Name); }
        }
    }

    public async Task<string?> TryRetraceAsync(string? version, string obfuscatedTrace, CancellationToken ct)
    {
        var cleanVersion = ErrorTicketService.CleanVersion(version);
        var directory = MappingDirectory;
        // Do not even invoke Retrace for historical tickets with no usable version or for versions never uploaded.
        if (cleanVersion is null || directory is null) return null;
        var mapping = Path.Combine(directory, cleanVersion + ".mapping.txt");
        if (!File.Exists(mapping)) return null;

        var traceFile = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".trace");
        try
        {
            await File.WriteAllTextAsync(traceFile, obfuscatedTrace, new UTF8Encoding(false), ct);
            var start = new ProcessStartInfo
            {
                FileName = RetraceExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(mapping);
            start.ArgumentList.Add(traceFile);
            start.ArgumentList.Add("--quiet");

            using var process = new Process { StartInfo = start };
            if (!process.Start()) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;
                try { await Task.WhenAll(stdout, stderr); } catch (IOException) { }
                logger.LogWarning("R8 Retrace timed out for Android version {AppVersion}.", cleanVersion);
                return null;
            }

            var output = await stdout;
            _ = await stderr;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                logger.LogWarning("R8 Retrace failed for Android version {AppVersion} (exit code {ExitCode}).", cleanVersion, process.ExitCode);
                return null;
            }
            return output.Length <= ErrorTicketService.MaxStackTraceLength * 2
                ? output
                : output[..(ErrorTicketService.MaxStackTraceLength * 2)];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Missing Java/SDK tools must never prevent the original crash from becoming a ticket.
            logger.LogWarning("R8 Retrace is unavailable for Android version {AppVersion} ({ExceptionType}); keeping the obfuscated trace.",
                cleanVersion, ex.GetType().Name);
            return null;
        }
        finally
        {
            try { if (File.Exists(traceFile)) File.Delete(traceFile); }
            catch (IOException ex) { logger.LogWarning("Could not remove a temporary Retrace input ({ExceptionType}).", ex.GetType().Name); }
        }
    }

    public static bool IsValidUploadKey(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
