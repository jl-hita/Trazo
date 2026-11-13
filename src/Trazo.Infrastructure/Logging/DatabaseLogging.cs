using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Trazo.Infrastructure.Logging;

/// <summary>A log entry waiting to be written to <c>app_logs</c>.</summary>
public sealed record AppLogEntry(
    DateTimeOffset Timestamp, int Level, string Category, string Message, string? Exception, string? TraceId);

/// <summary>
/// Bounded in-memory queue between the loggers (any thread, must never block or fail) and the writer. When the
/// writer cannot keep up the oldest entries are dropped.
/// </summary>
public sealed class AppLogQueue
{
    public const int Capacity = 10_000;
    private readonly Channel<AppLogEntry> _channel = Channel.CreateBounded<AppLogEntry>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<AppLogEntry> Reader => _channel.Reader;
    public bool TryWrite(AppLogEntry entry) => _channel.Writer.TryWrite(entry);
}

/// <summary>
/// Logger provider that keeps Information and above (the usual <c>Logging:LogLevel</c> filters still apply) so the
/// administration console can show them. It only enqueues; <see cref="DatabaseLogWriter"/> persists.
/// </summary>
[ProviderAlias("Database")]
public sealed class DatabaseLoggerProvider(AppLogQueue queue) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DatabaseLogger(categoryName, queue);
    public void Dispose() { }

    private sealed class DatabaseLogger(string category, AppLogQueue queue) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message;
            try { message = formatter(state, exception); }
            catch { message = "(the log message could not be formatted)"; }
            if (string.IsNullOrEmpty(message) && exception is null) return;
            queue.TryWrite(new AppLogEntry(
                TruncateToMicroseconds(DateTimeOffset.UtcNow), (int)logLevel, Trim(category, 200), Trim(message, 8_000) ?? "",
                exception is null ? null : Trim(exception.ToString(), 16_000), Activity.Current?.TraceId.ToString()));
        }
    }

    internal static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) => new(value.Ticks - value.Ticks % 10, TimeSpan.Zero);

    internal static string? Trim(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

/// <summary>
/// Persists queued log entries in batches and deletes the ones older than the retention period. A failure to write
/// logs never affects the application: the batch is dropped and a short note goes to the console.
/// </summary>
public sealed class DatabaseLogWriter(AppLogQueue queue, string connectionString, int retentionDays) : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(6);
    private DateTimeOffset _nextPurge = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFailureNote = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AppLogEntry>(BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queue.Reader.WaitToReadAsync(stoppingToken);
                await Task.Delay(FlushInterval, stoppingToken); // let a burst accumulate into one batch
            }
            catch (OperationCanceledException) { break; }

            await DrainAsync(batch, CancellationToken.None);
            await PurgeIfDueAsync();
        }
        await DrainAsync(batch, CancellationToken.None); // flush what is left on shutdown
    }

    private async Task DrainAsync(List<AppLogEntry> batch, CancellationToken cancellationToken)
    {
        while (true)
        {
            batch.Clear();
            while (batch.Count < BatchSize && queue.Reader.TryRead(out var entry)) batch.Add(entry);
            if (batch.Count == 0) return;
            await WriteAsync(batch, cancellationToken);
        }
    }

    private async Task WriteAsync(List<AppLogEntry> batch, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var npgsqlBatch = new NpgsqlBatch(connection);
            foreach (var e in batch)
            {
                var command = new NpgsqlBatchCommand(
                    "INSERT INTO app_logs (timestamp_utc, level, category, message, exception, trace_id) VALUES ($1, $2, $3, $4, $5, $6)");
                command.Parameters.AddWithValue(e.Timestamp);
                command.Parameters.AddWithValue((short)e.Level);
                command.Parameters.AddWithValue(e.Category);
                command.Parameters.AddWithValue(e.Message);
                command.Parameters.AddWithValue((object?)e.Exception ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)e.TraceId ?? DBNull.Value);
                npgsqlBatch.BatchCommands.Add(command);
            }
            await npgsqlBatch.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            NoteFailure(exception, batch.Count);
        }
    }

    private async Task PurgeIfDueAsync()
    {
        if (retentionDays <= 0 || DateTimeOffset.UtcNow < _nextPurge) return;
        _nextPurge = DateTimeOffset.UtcNow + PurgeInterval;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DELETE FROM app_logs WHERE timestamp_utc < $1", connection);
            command.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddDays(-retentionDays));
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception exception)
        {
            NoteFailure(exception, 0);
        }
    }

    // The logger cannot log its own failures (it would feed the queue), so at most one line a minute goes to stderr.
    private void NoteFailure(Exception exception, int dropped)
    {
        if (DateTimeOffset.UtcNow - _lastFailureNote < TimeSpan.FromMinutes(1)) return;
        _lastFailureNote = DateTimeOffset.UtcNow;
        Console.Error.WriteLine($"[trazo] Could not write logs to the database ({dropped} entries dropped): {exception.GetType().Name}: {exception.Message}");
    }
}

public static class DatabaseLoggingExtensions
{
    /// <summary>
    /// Stores application logs in PostgreSQL for the administration console. Disable with
    /// <c>Logging:Database:Enabled=false</c>; keep <c>Logging:Database:RetentionDays</c> days (default 30, 0 keeps all).
    /// </summary>
    public static IServiceCollection AddDatabaseLogging(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Trazo");
        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.Equals(configuration["Logging:Database:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
            return services;

        var retention = int.TryParse(configuration["Logging:Database:RetentionDays"], out var days) && days >= 0 ? days : 30;
        services.AddSingleton<AppLogQueue>();
        services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();
        services.AddHostedService(sp => new DatabaseLogWriter(sp.GetRequiredService<AppLogQueue>(), connectionString, retention));
        return services;
    }
}
