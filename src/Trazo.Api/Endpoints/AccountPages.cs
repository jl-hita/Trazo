using System.Net;
using System.Security.Cryptography;
using System.Text;
using Trazo.Application.Localization;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The public, self-contained pages of the web account deletion flow (the URL given to Google Play). Plain HTML forms, no
/// JavaScript and no external resources, so the Content-Security-Policy can be strict: the only inline code is one stylesheet,
/// allowed by its hash.
/// </summary>
public static class AccountPages
{
    private const string Css = """
        :root{color-scheme:light dark;--bg:#f6f7f9;--fg:#1b2430;--muted:#4b5a6d;--card:#fff;--line:#c9d1dc;--accent:#1d4f91;--danger:#a61b1b}
        @media (prefers-color-scheme:dark){:root{--bg:#10151c;--fg:#e8edf5;--muted:#a9b6c7;--card:#18202b;--line:#34465b;--accent:#7fb0ee;--danger:#ff9b9b}}
        body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.55 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        main{max-width:38rem;margin:0 auto;padding:2rem 1rem 3rem}
        h1{font-size:1.6rem;margin:0 0 .5rem}h2{font-size:1.1rem;margin:1.6rem 0 .4rem}
        p,li{color:var(--fg)}.muted{color:var(--muted)}
        .card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:1.2rem}
        label{display:block;font-weight:600;margin:.8rem 0 .3rem}
        input[type=email],input[type=text]{box-sizing:border-box;width:100%;padding:.65rem .75rem;font:inherit;color:var(--fg);background:var(--bg);border:2px solid var(--line);border-radius:8px}
        input:focus,button:focus{outline:3px solid var(--accent);outline-offset:2px}
        button{margin-top:1rem;padding:.7rem 1.2rem;font:inherit;font-weight:700;border:0;border-radius:8px;color:#fff;background:var(--accent);cursor:pointer}
        button.danger{background:var(--danger);color:#fff}
        .alert{border-left:5px solid var(--danger);padding:.5rem .8rem;margin:1rem 0;background:var(--card)}
        .ok{border-left:5px solid var(--accent);padding:.5rem .8rem;margin:1rem 0;background:var(--card)}
        """;

    public static readonly string CspStyleHash = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Css)));

    public static string ContentSecurityPolicy =>
        $"default-src 'none'; style-src '{CspStyleHash}'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    /// <summary>The page in the language of the visitor (Spanish unless the browser asked for English).</summary>
    private static string Pick(AppLanguage language, string es, string en) => language == AppLanguage.En ? en : es;

    private static string Layout(AppLanguage language, string title, string body) => $"""
        <!doctype html>
        <html lang="{(language == AppLanguage.En ? "en" : "es")}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>{E(title)} · TRAZO</title>
        <style>{Css}</style>
        </head>
        <body>
        <main>
        {body}
        </main>
        </body>
        </html>
        """;

    private static string WhatIsDeleted(AppLanguage language) => language == AppLanguage.En ? WhatIsDeletedEn : WhatIsDeletedEs;

    private const string WhatIsDeletedEs = """
        <h2>Qué se elimina</h2>
        <ul>
        <li>Tu cuenta: correo, nombre, contraseña, vinculación con Google y verificación en dos pasos.</li>
        <li>Tus planes de entrenamiento y todos tus registros de sesiones, incluidas las notas y el dolor o las limitaciones que hayas indicado.</li>
        <li>Las adaptaciones del plan, tus dispositivos y sesiones abiertas, y los datos de tu suscripción guardados en TRAZO.</li>
        </ul>
        <p>La eliminación es inmediata e irreversible. TRAZO no conserva copia de estos datos.</p>
        <h2>Suscripción de Google Play</h2>
        <p><strong>Eliminar la cuenta no cancela tu suscripción de Google Play.</strong> Si tienes una, cancélala en Google Play
        (Play Store &rarr; Pagos y suscripciones &rarr; Suscripciones) para no seguir pagando.</p>
        """;

    private const string WhatIsDeletedEn = """
        <h2>What is deleted</h2>
        <ul>
        <li>Your account: email, name, password, Google link and two-step verification.</li>
        <li>Your training plans and all your session logs, including the notes and any pain or limitations you reported.</li>
        <li>The adaptations of your plan, your devices and open sessions, and your subscription data kept in TRAZO.</li>
        </ul>
        <p>Deletion is immediate and irreversible. TRAZO keeps no copy of this data.</p>
        <h2>Google Play subscription</h2>
        <p><strong>Deleting the account does not cancel your Google Play subscription.</strong> If you have one, cancel it in Google Play
        (Play Store &rarr; Payments &amp; subscriptions &rarr; Subscriptions) so you stop paying.</p>
        """;

    public static string RequestForm(string? error = null, AppLanguage language = AppLanguage.Es) => language == AppLanguage.En ? RequestFormEn(error) : RequestFormEs(error);

    private static string RequestFormEn(string? error) => Layout(AppLanguage.En, "Delete account", $"""
        <h1>Delete my TRAZO account</h1>
        <p class="muted">You can also do it from the app settings. Here you can do it without opening it.</p>
        {(error is null ? "" : $"<p class=\"alert\" role=\"alert\">{E(error)}</p>")}
        <form class="card" method="post" action="/api/v1/account/deletion-request">
        <label for="email">Email of your account</label>
        <input id="email" name="email" type="email" autocomplete="email" required maxlength="320">
        <p class="muted">We will send you a confirmation link that expires in 30 minutes. Nothing is deleted until you open it and confirm.</p>
        <button type="submit">Send me the link</button>
        </form>
        {WhatIsDeletedEn}
        """);

    private static string RequestFormEs(string? error = null) => Layout(AppLanguage.Es, "Eliminar cuenta", $"""
        <h1>Eliminar mi cuenta de TRAZO</h1>
        <p class="muted">También puedes hacerlo desde los ajustes de la app. Aquí puedes hacerlo sin abrirla.</p>
        {(error is null ? "" : $"<p class=\"alert\" role=\"alert\">{E(error)}</p>")}
        <form class="card" method="post" action="/api/v1/account/deletion-request">
        <label for="email">Correo electrónico de tu cuenta</label>
        <input id="email" name="email" type="email" autocomplete="email" required maxlength="320">
        <p class="muted">Te enviaremos un enlace de confirmación que caduca en 30 minutos. No se elimina nada hasta que lo abras y confirmes.</p>
        <button type="submit">Enviarme el enlace</button>
        </form>
        {WhatIsDeletedEs}
        """);

    public static string RequestReceived(AppLanguage language = AppLanguage.Es) => language == AppLanguage.En
        ? Layout(language, "Check your email", """
            <h1>Check your email</h1>
            <p class="ok" role="status">If an account exists with that email, we have sent you a link to confirm the deletion. It expires in 30 minutes.</p>
            <p class="muted">If you do not receive it, check your spam folder. If you did not request this, ignore the message: nothing will be deleted.</p>
            """)
        : Layout(language, "Revisa tu correo", """
            <h1>Revisa tu correo</h1>
            <p class="ok" role="status">Si existe una cuenta con ese correo, te hemos enviado un enlace para confirmar la eliminación. Caduca en 30 minutos.</p>
            <p class="muted">Si no lo recibes, revisa la carpeta de spam. Si no has solicitado esto, ignora el mensaje: no se borrará nada.</p>
            """);

    public static string Confirm(string token, string maskedEmail, bool needsCode, string? error = null, AppLanguage language = AppLanguage.Es) =>
        Layout(language, Pick(language, "Confirmar eliminación", "Confirm deletion"), $"""
        <h1>{Pick(language, "Confirma la eliminación", "Confirm the deletion")}</h1>
        <p>{Pick(language, "Vas a eliminar de forma definitiva la cuenta", "You are about to permanently delete the account")} <strong>{E(maskedEmail)}</strong> {Pick(language, "y todos sus datos.", "and all its data.")}</p>
        {(error is null ? "" : $"<p class=\"alert\" role=\"alert\">{E(error)}</p>")}
        <form class="card" method="post" action="/api/v1/account/deletion-request/confirm">
        <input type="hidden" name="token" value="{E(token)}">
        {(needsCode ? $"<label for=\"code\">{Pick(language, "Código de verificación en dos pasos (o código de recuperación)", "Two-step verification code (or recovery code)")}</label><input id=\"code\" name=\"code\" type=\"text\" autocomplete=\"one-time-code\" inputmode=\"numeric\" required maxlength=\"32\">" : "")}
        <button class="danger" type="submit">{Pick(language, "Eliminar definitivamente mi cuenta", "Permanently delete my account")}</button>
        </form>
        {WhatIsDeleted(language)}
        """);

    public static string Done(AppLanguage language = AppLanguage.Es) => language == AppLanguage.En
        ? Layout(language, "Account deleted", """
            <h1>Account deleted</h1>
            <p class="ok" role="status">Your account and your data have been deleted from TRAZO.</p>
            <p>Remember to cancel your Google Play subscription if you had one: deleting the account does not cancel it.</p>
            """)
        : Layout(language, "Cuenta eliminada", """
            <h1>Cuenta eliminada</h1>
            <p class="ok" role="status">Tu cuenta y tus datos se han eliminado de TRAZO.</p>
            <p>Recuerda cancelar tu suscripción de Google Play si tenías una: eliminar la cuenta no la cancela.</p>
            """);

    public static string Invalid(AppLanguage language = AppLanguage.Es) => language == AppLanguage.En
        ? Layout(language, "Invalid link", """
            <h1>Invalid link</h1>
            <p class="alert" role="alert">The link is not valid, has already been used or has expired.</p>
            <p><a href="/account/delete">Request a new link</a></p>
            """)
        : Layout(language, "Enlace no válido", """
            <h1>Enlace no válido</h1>
            <p class="alert" role="alert">El enlace no es válido, ya se ha usado o ha caducado.</p>
            <p><a href="/account/delete">Solicitar un enlace nuevo</a></p>
            """);

    public static string Blocked(string message, AppLanguage language = AppLanguage.Es) =>
        Layout(language, Pick(language, "No se puede eliminar", "Cannot delete"), $"""
        <h1>{Pick(language, "No se puede eliminar la cuenta", "The account cannot be deleted")}</h1>
        <p class="alert" role="alert">{E(Localizer.Translate(message, language))}</p>
        """);
}
