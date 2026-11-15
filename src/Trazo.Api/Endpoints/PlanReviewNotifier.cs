using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Email notices for the review conversations. They are sent in the background and never carry the text of a message (email is
/// not a safe place for health information): they only say that there is something to read. A failure is logged and ignored,
/// because everything is also in the console and the app.
/// </summary>
internal static class PlanReviewNotifier
{
    /// <summary>Push is an extra channel: when it is off, not configured or failing, the email and the in-app view still work.</summary>
    private static async Task PushAsync(IServiceScope scope, ILogger logger, IReadOnlyCollection<Guid> userIds, PushMessage message)
    {
        if (userIds.Count == 0) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<IPushSender>().SendAsync(userIds, message, CancellationToken.None);
        }
        catch (PushException ex) when (ex.Failure == PushFailure.NotConfigured)
        {
            // Not set up (yet): nothing to report.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send a push notification for a plan review.");
        }
    }

    public static Task NotifyStaffAsync(IServiceScopeFactory scopes, ILoggerFactory loggerFactory, Guid requestId, Guid userId, bool isNew)
    {
        var logger = loggerFactory.CreateLogger("Trazo.PlanReview");
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
                var request = await db.PlanReviewRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId);
                if (request is null) return;
                var userName = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? "Un usuario";
                var staff = db.Users.AsNoTracking().Where(u => (u.Role == "admin" || u.Role == "superadmin") && u.DisabledAt == null && u.EmailVerifiedAt != null);
                // Somebody has it: tell only them. Otherwise it is for whoever wants to take it.
                var recipients = request.Status == PlanReviewStatus.InProgress && request.AssignedToUserId is { } assigned
                    ? await staff.Where(u => u.Id == assigned).Select(u => u.Email).ToListAsync()
                    : await staff.Select(u => u.Email).ToListAsync();
                var staffIds = request.Status == PlanReviewStatus.InProgress && request.AssignedToUserId is { } owner
                    ? await staff.Where(u => u.Id == owner).Select(u => u.Id).ToListAsync()
                    : await staff.Select(u => u.Id).ToListAsync();
                await PushAsync(scope, logger, staffIds, new PushMessage("TRAZO",
                    isNew ? "Nueva petición de revisión de rutina." : "Nuevo mensaje en una revisión de rutina.", isNew ? "review-new" : "review-message"));
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var subject = isNew ? "TRAZO: nueva petición de revisión de rutina" : "TRAZO: nuevo mensaje en una revisión de rutina";
                var text = isNew
                    ? $"{userName} ha pedido que se revise su rutina a mano.\n\nÁbrela en la consola de administración (Plan reviews) para leer el mensaje y tomarla."
                    : $"{userName} ha escrito en su petición de revisión.\n\nLéelo en la consola de administración (Plan reviews).";
                foreach (var to in recipients) await email.SendAsync(to, subject, text, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify the administrators of a plan review message; it is still in the console.");
            }
        });
        return Task.CompletedTask;
    }

    public static Task NotifyUserAsync(IServiceScopeFactory scopes, ILoggerFactory loggerFactory, Guid userId, bool closed)
    {
        var logger = loggerFactory.CreateLogger("Trazo.PlanReview");
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
                var to = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.DisabledAt == null && u.EmailVerifiedAt != null)
                    .Select(u => u.Email).FirstOrDefaultAsync();
                if (to is null) return;
                // The text never says what the team wrote; the app translates it from the kind.
                await PushAsync(scope, logger, [userId], new PushMessage("TRAZO",
                    closed ? "Hemos terminado de revisar tu rutina." : "Tienes un mensaje del equipo.", closed ? "review-closed" : "review-reply"));
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await email.SendAsync(to,
                    closed ? "TRAZO: hemos terminado de revisar tu rutina" : "TRAZO: tienes un mensaje del equipo",
                    closed
                        ? "El equipo ha terminado la revisión manual de tu rutina. Abre la app (Cuenta → Revisión manual) para leer su respuesta."
                        : "El equipo ha respondido a tu petición de revisión. Abre la app (Cuenta → Revisión manual) para leerlo.",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify a user of a plan review reply; it is still in the app.");
            }
        });
        return Task.CompletedTask;
    }
}
