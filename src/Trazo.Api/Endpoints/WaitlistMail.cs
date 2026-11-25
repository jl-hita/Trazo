using Trazo.Application.Localization;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The two emails of the waitlist: the confirmation (double opt-in) and the invitation to the closed test. Plain text, in the
/// language of the landing page the person used. Neither contains anything but the links and the explanation.
/// </summary>
public static class WaitlistMail
{
    public static (string Subject, string Body) Confirmation(string language, string confirmUrl)
    {
        var t = In(language);
        return (t("Confirma tu correo para la lista de TRAZO"),
            $"{t("Hola,")}\n\n{t("Alguien (esperamos que tú) ha pedido apuntar este correo a la lista de TRAZO. Con ella te avisaremos del lanzamiento y te invitaremos a probar la app en la prueba cerrada de Google Play.")}\n\n" +
            $"{t("Para confirmarlo, abre este enlace y pulsa «Confirmar»:")}\n\n{confirmUrl}\n\n" +
            $"{t("El enlace caduca en 48 horas. Si no has sido tú, ignora este mensaje: no te escribiremos más.")}\n\n" +
            $"{t("Para probar la app necesitarás usar en Google Play la cuenta de Google asociada a este correo.")}\n\n{t("El equipo de TRAZO")}");
    }

    public static (string Subject, string Body) Invitation(string language, string testUrl, string unsubscribeUrl)
    {
        var t = In(language);
        return (t("Ya puedes probar TRAZO en Google Play"),
            $"{t("Hola,")}\n\n{t("Gracias por apuntarte a la lista. Ya puedes probar TRAZO en la prueba cerrada de Google Play:")}\n\n" +
            $"{t("1. En tu móvil, entra en Google Play con la cuenta de Google asociada a este correo.")}\n" +
            $"{t("2. Abre este enlace y pulsa «Hacerme tester»:")}\n   {testUrl}\n" +
            $"{t("3. Después, instala TRAZO desde Google Play (puede tardar unos minutos en aparecer).")}\n\n" +
            $"{t("Si no quieres recibir más mensajes de TRAZO, puedes borrar tus datos aquí:")}\n{unsubscribeUrl}\n\n{t("El equipo de TRAZO")}");
    }

    private static Func<string, string> In(string language)
    {
        var chosen = LanguageNegotiation.FromTag(language) ?? AppLanguage.En;
        return spanish => Localizer.Format(chosen, spanish);
    }
}
