namespace Trazo.Api.Endpoints;

/// <summary>
/// The two emails of the waitlist: the confirmation (double opt-in) and the invitation to the closed test. Plain text, in the
/// language of the landing page the person used. Neither contains anything but the links and the explanation.
/// </summary>
public static class WaitlistMail
{
    public static (string Subject, string Body) Confirmation(string language, string confirmUrl) => language == "en"
        ? ("Confirm your email for the TRAZO list",
            "Hi,\n\nSomeone (we hope you) asked to add this email address to the TRAZO list. With it we will tell you when the app launches " +
            "and invite you to try it in the closed test on Google Play.\n\n" +
            $"To confirm, open this link and press \"Confirm\":\n\n{confirmUrl}\n\n" +
            "The link expires in 48 hours. If it was not you, ignore this message: we will not write to you again.\n\n" +
            "To try the app you will need to use, on Google Play, the Google account linked to this email address.\n\nThe TRAZO team")
        : ("Confirma tu correo para la lista de TRAZO",
            "Hola,\n\nAlguien (esperamos que tú) ha pedido apuntar este correo a la lista de TRAZO. Con ella te avisaremos del lanzamiento " +
            "y te invitaremos a probar la app en la prueba cerrada de Google Play.\n\n" +
            $"Para confirmarlo, abre este enlace y pulsa «Confirmar»:\n\n{confirmUrl}\n\n" +
            "El enlace caduca en 48 horas. Si no has sido tú, ignora este mensaje: no te escribiremos más.\n\n" +
            "Para probar la app necesitarás usar en Google Play la cuenta de Google asociada a este correo.\n\nEl equipo de TRAZO");

    public static (string Subject, string Body) Invitation(string language, string testUrl, string unsubscribeUrl) => language == "en"
        ? ("You can now try TRAZO on Google Play",
            "Hi,\n\nThanks for joining the list. You can now try TRAZO in the closed test on Google Play:\n\n" +
            "1. On your phone, sign in to Google Play with the Google account linked to this email address.\n" +
            $"2. Open this link and press \"Become a tester\":\n   {testUrl}\n" +
            "3. Then install TRAZO from Google Play (it can take a few minutes to appear).\n\n" +
            $"If you do not want to hear from TRAZO any more, you can delete your data here:\n{unsubscribeUrl}\n\nThe TRAZO team")
        : ("Ya puedes probar TRAZO en Google Play",
            "Hola,\n\nGracias por apuntarte a la lista. Ya puedes probar TRAZO en la prueba cerrada de Google Play:\n\n" +
            "1. En tu móvil, entra en Google Play con la cuenta de Google asociada a este correo.\n" +
            $"2. Abre este enlace y pulsa «Hacerme tester»:\n   {testUrl}\n" +
            "3. Después, instala TRAZO desde Google Play (puede tardar unos minutos en aparecer).\n\n" +
            $"Si no quieres recibir más mensajes de TRAZO, puedes borrar tus datos aquí:\n{unsubscribeUrl}\n\nEl equipo de TRAZO");
}
