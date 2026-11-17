namespace Trazo.Application.Localization;

/// <summary>
/// The texts of authentication, account, e-mail and the other app-facing endpoints. Each pair is (Spanish, English); most were written
/// in Spanish in the code and a few (the authentication errors) in English, so both directions exist and the answer is in the
/// language of the request whichever language the code used. <c>{0}</c> are interpolated values.
/// </summary>
internal static class AccountCatalog
{
    internal static readonly (string Es, string En)[] Pairs =
    [
        // e-mails
        ("Ya tienes una cuenta en TRAZO", "You already have a TRAZO account"),
        ("Alguien ha intentado crear una cuenta de TRAZO con este correo, pero ya existe una.",
         "Someone tried to create a TRAZO account with this email address, but one already exists."),
        ("Si has sido tú, inicia sesión o usa la opción de contraseña olvidada.", "If it was you, sign in or use the forgotten password option."),
        ("Si no has sido tú, ignora este mensaje: no ha cambiado nada.", "If it was not you, ignore this message: nothing has changed."),
        ("Verifica tu correo de TRAZO", "Verify your TRAZO email"),
        ("Para activar tu cuenta, abre este enlace:", "To activate your account, open this link:"),
        ("Verifica tu cuenta usando este enlace:", "Verify your account using this link:"),
        ("Restablece tu contraseña de TRAZO", "Reset your TRAZO password"),
        ("Usa este enlace para restablecer tu contraseña:", "Use this link to reset your password:"),
        ("Invitación a TRAZO", "Invitation to TRAZO"),
        ("Has sido invitado a TRAZO.", "You have been invited to TRAZO."),
        ("Abre este enlace para activar tu cuenta y establecer tu contraseña:", "Open this link to activate your account and set your password:"),
        ("TRAZO: prueba de correo", "TRAZO: email test"),
        ("Este es un correo de prueba de TRAZO.", "This is a TRAZO test email."),
        ("Si lo has recibido, la configuración SMTP es correcta.", "If you received it, the SMTP configuration is correct."),
        ("Esta solicitud se hizo {0} el {1} a las {2} (hora UTC).", "This request was made {0} on {1} at {2} (UTC)."),
        ("desde la app de TRAZO", "from the TRAZO app"),
        ("desde la web", "from the web"),
        ("desde un origen desconocido", "from an unknown origin"),
        ("El enlace caduca en {0} minutos.", "The link expires in {0} minutes."),
        ("El enlace caduca en {0} minutos y solo se puede usar una vez.", "The link expires in {0} minutes and can only be used once."),
        ("Si no has solicitado esta acción, ignora este mensaje.", "If you did not request this action, ignore this message."),
        ("Confirma la eliminación de tu cuenta de TRAZO", "Confirm the deletion of your TRAZO account"),
        ("Has pedido eliminar tu cuenta de TRAZO y todos sus datos.", "You asked to delete your TRAZO account and all its data."),
        ("Para confirmarlo, abre este enlace:", "To confirm it, open this link:"),
        ("Si no has sido tú, ignora este mensaje: no se borrará nada.", "If it was not you, ignore this message: nothing will be deleted."),
        ("Eliminar la cuenta no cancela tu suscripción de Google Play; cancélala en Google Play si tenías una.",
         "Deleting the account does not cancel your Google Play subscription; cancel it in Google Play if you had one."),
        // answers
        ("Verifica tu correo antes de iniciar sesión.", "Verify your email before signing in."),
        ("Si existe una cuenta pendiente de verificación, enviaremos un correo.", "If there is an account pending verification, we will send an email."),
        ("Si existe una cuenta con ese correo, enviaremos un enlace de confirmación.", "If an account exists with that email, we will send a confirmation link."),
        ("La contraseña, el token de Google o el código no son válidos.", "The password, the Google token or the code are not valid."),
        ("Eres el último superadministrador: nombra a otro antes de eliminar la cuenta.", "You are the last superadministrator: appoint another one before deleting the account."),
        ("El enlace no es válido o ha caducado.", "The link is not valid or has expired."),
        ("El código no es válido.", "The code is not valid."),
        ("Use al menos 12 caracteres, con mayúsculas, minúsculas y números.", "Use at least 12 characters including uppercase, lowercase and a number."),
        ("Tu suscripción de Google Play NO se cancela al eliminar la cuenta: seguirás pagando hasta que la canceles en Google Play.",
         "Your Google Play subscription is NOT cancelled when you delete the account: you will keep paying until you cancel it in Google Play."),
        // written in English in the code
        ("Se necesita un correo y un nombre válidos.", "A valid email and display name are required."),
        ("Ya hay una cuenta con este correo. Inicia sesión en ella y vincula Google desde sus ajustes de seguridad.",
         "An account already uses this email. Sign in to that account and link Google from its security settings."),
        ("El correo verificado de Google debe coincidir con el de esta cuenta de TRAZO.", "The verified Google email must match the email on this TRAZO account."),
        ("Esta cuenta de Google ya está vinculada a otra cuenta de TRAZO.", "This Google account is already linked to another TRAZO account."),
        ("Desactiva la verificación en dos pasos antes de cambiar su autenticador.", "Disable two-factor authentication before replacing its authenticator."),
        ("Empieza primero la configuración de la verificación en dos pasos.", "Start two-factor setup first."),
        ("El código del autenticador no es válido o ya se ha usado.", "The authenticator code is invalid or has already been used."),
        ("La verificación en dos pasos no está activada.", "Two-factor authentication is not enabled."),
        ("La configuración inicial no se ha completado.", "The initial setup has not been completed."),
        ("El enlace de verificación no es válido o ha caducado.", "The verification link is invalid or expired."),
        ("Se esperaba un token de registro.", "Expected a registration token."),
        ("Se necesita un identificador de ejercicio válido.", "A valid exercise ID is required."),
        ("Esta compra está vinculada a otra cuenta.", "This purchase is linked to another account."),
        ("Esta compra la hizo otra cuenta.", "This purchase was made by another account."),
        ("La compra es de otro producto.", "The purchase is for a different product."),
        ("Google Play no reconoce esta compra.", "Google Play does not recognise this purchase."),
        ("Las suscripciones aún no están disponibles.", "Subscriptions are not available yet."),
        ("No se pudo verificar la compra. Inténtalo más tarde.", "The purchase could not be verified. Try again later."),
        ("El enlace no es válido, ha caducado o ya se ha usado.", "The link is invalid, expired or already used."),
        ("El enlace no es válido.", "The link is invalid."),
        ("Error inesperado.", "Unexpected error."),
    ];

    /// <summary>Texts written in Spanish, to English.</summary>
    internal static IEnumerable<(string, string)> SpanishToEnglish => Pairs.Select(p => (p.Es, p.En));

    /// <summary>Texts written in English, to Spanish.</summary>
    internal static IEnumerable<(string, string)> EnglishToSpanish => Pairs.Select(p => (p.En, p.Es));
}
