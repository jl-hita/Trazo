using System.Threading.Channels;

namespace Trazo.Api.Support;

/// <summary>
/// Work an endpoint starts but does not wait for (a confirmation email whose delay must not reveal whether an address exists, a notice
/// to the administrators). It replaces <c>Task.Run</c>, whose failures were lost, whose scope could end under it and which nobody
/// waited for at shutdown. A job gets its own scope, runs in the hosting process and reports what it cannot finish.
/// </summary>
public interface IBackgroundWork
{
    /// <summary>Queues the job. False when the queue is full (the rejection is reported as an error ticket).</summary>
    bool Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> job);
}

public sealed class BackgroundWorkQueue(IErrorReporter reporter, ILoggerFactory loggers) : IBackgroundWork
{
    public const int Capacity = 500;
    private readonly Channel<(string Name, Func<IServiceProvider, CancellationToken, Task> Job)> _channel =
        Channel.CreateBounded<(string, Func<IServiceProvider, CancellationToken, Task>)>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private int _inFlight;

    /// <summary>Jobs accepted and not finished yet (for tests and diagnostics: zero means nothing is pending).</summary>
    public int InFlight => Volatile.Read(ref _inFlight);
    internal void Finished() => Interlocked.Decrement(ref _inFlight);

    internal ChannelReader<(string Name, Func<IServiceProvider, CancellationToken, Task> Job)> Reader => _channel.Reader;
    internal void Complete() => _channel.Writer.TryComplete();

    public bool Enqueue(string name, Func<IServiceProvider, CancellationToken, Task> job)
    {
        Interlocked.Increment(ref _inFlight);
        if (_channel.Writer.TryWrite((name, job))) return true;
        Interlocked.Decrement(ref _inFlight);
        loggers.CreateLogger("Trazo.BackgroundWork").LogWarning("Background job {Job} was rejected: the queue is full or stopped.", name);
        reporter.Report(new InvalidOperationException($"The background work queue rejected the job '{name}'."));
        return false;
    }
}

/// <summary>
/// Runs the queued jobs, a few at a time. A job that fails is logged by type only (the text of a mail error can quote an address) and
/// reported as an error ticket unless it is an expected operational condition (see <see cref="ErrorMarks.AsExpected{T}"/>). On a normal
/// shutdown the jobs that were already accepted are finished, within what the host allows.
/// </summary>
public sealed class BackgroundWorkService(BackgroundWorkQueue queue, IServiceScopeFactory scopes, IErrorReporter reporter, ILoggerFactory loggers) : BackgroundService
{
    private const int MaxParallel = 4;
    private readonly ILogger _logger = loggers.CreateLogger("Trazo.BackgroundWork");

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(queue.Reader.ReadAllAsync(CancellationToken.None), new ParallelOptions { MaxDegreeOfParallelism = MaxParallel },
            async (item, _) => await RunAsync(item.Name, item.Job));

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        return base.StopAsync(cancellationToken);
    }

    private async Task RunAsync(string name, Func<IServiceProvider, CancellationToken, Task> job)
    {
        try
        {
            // Decremented in the finally below, after the failure (if any) has been reported.
            using var scope = scopes.CreateScope();
            await job(scope.ServiceProvider, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Nothing is awaiting this job, so an exception that escaped here would be lost: it ends up in the log and the ticket system.
            var expected = ex is OperationCanceledException || ErrorMarks.IsExpected(ex);
            _logger.LogWarning("Background job {Job} failed ({ExceptionType}); {Ticket}.", name, ex.GetType().FullName,
                expected ? "expected condition, no ticket" : "reported as an error ticket");
            if (!expected) await reporter.ReportAsync(ex, null, CancellationToken.None);
        }
        finally { queue.Finished(); }
    }
}

public static class BackgroundWorkExtensions
{
    public static IServiceCollection AddBackgroundWork(this IServiceCollection services)
    {
        services.AddSingleton<BackgroundWorkQueue>();
        services.AddSingleton<IBackgroundWork>(sp => sp.GetRequiredService<BackgroundWorkQueue>());
        services.AddHostedService<BackgroundWorkService>();
        return services;
    }
}
