using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Settings;

/// <summary>Outcome of the last attempt to send an email. <see cref="Error"/> is only the exception type, never a message.</summary>
public sealed record EmailDeliveryResult(bool Succeeded, DateTimeOffset At, string? Error);

/// <summary>
/// Remembers whether the last SMTP send worked, so the administration console can warn when transactional email
/// (verification, password recovery) has stopped working. It keeps a single row in <c>app_settings</c> and holds no
/// recipient, subject or server message.
/// </summary>
public interface IEmailDeliveryRecorder
{
    Task RecordAsync(Exception? failure, CancellationToken ct = default);
}

public sealed class EmailDeliveryRecorder(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<EmailDeliveryRecorder>? logger = null) : IEmailDeliveryRecorder
{
    public const string Key = "Email:Smtp:LastSend";

    public async Task RecordAsync(Exception? failure, CancellationToken ct = default)
    {
        try
        {
            // Its own scope (and context): saving here must never flush pending changes of the request that sends the email.
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var now = clock.GetUtcNow();
            var value = Serialize(new EmailDeliveryResult(failure is null, now, failure?.GetType().Name));
            var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
            if (row is null) db.AppSettings.Add(new AppSetting { Key = Key, Value = value, IsSecret = false, UpdatedAt = now });
            else { row.Value = value; row.UpdatedAt = now; }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Recording is a diagnostic aid: it must never break (or hide the outcome of) the send itself.
            logger?.LogWarning("The result of the email send could not be recorded ({Type}).", exception.GetType().Name);
        }
    }

    public static string Serialize(EmailDeliveryResult result) =>
        JsonSerializer.Serialize(new Stored(result.Succeeded, result.At, result.Error));

    public static EmailDeliveryResult? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(value);
            return stored is null ? null : new EmailDeliveryResult(stored.Ok, stored.At, stored.Error);
        }
        catch (JsonException) { return null; }
    }

    private sealed record Stored(bool Ok, DateTimeOffset At, string? Error);
}
