using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Api.Support;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Email notices for the review conversations. They are sent by the background service and never carry the text of a message (email is
/// not a safe place for health information): they only say that there is something to read. A failure does not undo anything, because
/// everything is also in the console and the app, but it is not lost either: the background service logs it and reports it as an error
/// ticket unless the mail server was the one that did not answer.
/// </summary>
internal static class PlanReviewNotifier
{
    /// <summary>
    /// Push is an extra channel: when it is off, not configured or failing, the email and the in-app view still work, so a failure here
    /// must not stop the email. It is still reported when it is a defect (not configured and Firebase being unreachable are not).
    /// </summary>
    private static async Task PushAsync(IServiceProvider services, IReadOnlyCollection<Guid> userIds, PushMessage message)
    {
        if (userIds.Count == 0) return;
        try
        {
            await services.GetRequiredService<IPushSender>().SendAsync(userIds, message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (ex is PushException { Failure: PushFailure.NotConfigured or PushFailure.Unauthorized } or PushException { InnerException: not null })
                ex.AsExpected();
            await services.GetRequiredService<IErrorReporter>().ReportAsync(ex, null, CancellationToken.None);
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Trazo.PlanReview")
                .LogWarning("Could not send a push notification for a plan review ({ExceptionType}).", ex.GetType().Name);
        }
    }

    public static void NotifyStaff(IBackgroundWork background, Guid requestId, Guid userId, bool isNew) =>
        background.Enqueue("plan-review-staff-notice", async (services, ct) =>
        {
            var db = services.GetRequiredService<TrazoDbContext>();
            var request = await db.PlanReviewRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (request is null) return;
            var userName = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Un usuario";
            var staff = db.Users.AsNoTracking().Where(u => (u.Role == "admin" || u.Role == "superadmin") && u.DisabledAt == null && u.EmailVerifiedAt != null);
            // Somebody has it: tell only them. Otherwise it is for whoever wants to take it.
            var recipients = request.Status == PlanReviewStatus.InProgress && request.AssignedToUserId is { } assigned
                ? await staff.Where(u => u.Id == assigned).Select(u => u.Email).ToListAsync(ct)
                : await staff.Select(u => u.Email).ToListAsync(ct);
            var staffIds = request.Status == PlanReviewStatus.InProgress && request.AssignedToUserId is { } owner
                ? await staff.Where(u => u.Id == owner).Select(u => u.Id).ToListAsync(ct)
                : await staff.Select(u => u.Id).ToListAsync(ct);
            await PushAsync(services, staffIds, new PushMessage("TRAZO",
                isNew ? "Nueva petición de revisión de rutina." : "Nuevo mensaje en una revisión de rutina.", isNew ? "review-new" : "review-message"));
            var email = services.GetRequiredService<IEmailSender>();
            var subject = isNew ? "TRAZO: nueva petición de revisión de rutina" : "TRAZO: nuevo mensaje en una revisión de rutina";
            var text = isNew
                ? $"{userName} ha pedido que se revise su rutina a mano.\n\nÁbrela en la consola de administración (Plan reviews) para leer el mensaje y tomarla."
                : $"{userName} ha escrito en su petición de revisión.\n\nLéelo en la consola de administración (Plan reviews).";
            foreach (var to in recipients) await email.SendAsync(to, subject, text, ct);
        });

    public static void NotifyUser(IBackgroundWork background, Guid userId, bool closed) =>
        background.Enqueue("plan-review-user-notice", async (services, ct) =>
        {
            var db = services.GetRequiredService<TrazoDbContext>();
            var target = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.DisabledAt == null && u.EmailVerifiedAt != null)
                .Select(u => new { u.Email, u.Language }).FirstOrDefaultAsync(ct);
            if (target is null) return;
            var to = target.Email;
            // The language the user chose (Spanish when they did not: there is no request to ask).
            var language = Trazo.Application.Localization.LanguageNegotiation.FromChoice(target.Language) ?? Trazo.Application.Localization.AppLanguage.Es;
            string Tr(string text) => Trazo.Application.Localization.Localizer.Translate(text, language)!;
            // The text never says what the team wrote; the app translates it from the kind.
            await PushAsync(services, [userId], new PushMessage("TRAZO",
                Tr(closed ? "Hemos terminado de revisar tu rutina." : "Tienes un mensaje del equipo."), closed ? "review-closed" : "review-reply"));
            var email = services.GetRequiredService<IEmailSender>();
            await email.SendAsync(to,
                Tr(closed ? "TRAZO: hemos terminado de revisar tu rutina" : "TRAZO: tienes un mensaje del equipo"),
                closed
                    ? Tr("El equipo ha terminado la revisión manual de tu rutina.") + " " + Tr("Abre la app (Cuenta → Revisión manual) para leer su respuesta.")
                    : Tr("El equipo ha respondido a tu petición de revisión.") + " " + Tr("Abre la app (Cuenta → Revisión manual) para leerlo."),
                ct);
        });
}
