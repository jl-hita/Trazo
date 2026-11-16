namespace Trazo.Api.Security;

public enum RequestOriginKind { App, Web, Unknown }

/// <summary>
/// Where a public request came from, guessed from the User-Agent: the Android app (OkHttp), a browser, or something else
/// (a script, a bot, no header). It is only a hint for the audit log and for the emails that say where a request came from:
/// the header can be forged, so it must never be used to allow or refuse anything.
/// </summary>
public static class RequestOrigin
{
    public static RequestOriginKind Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return RequestOriginKind.Unknown;
        if (userAgent.Contains("okhttp", StringComparison.OrdinalIgnoreCase)) return RequestOriginKind.App;
        if (userAgent.StartsWith("Mozilla/", StringComparison.OrdinalIgnoreCase)) return RequestOriginKind.Web;
        return RequestOriginKind.Unknown;
    }

    /// <summary>The short tag written to the log.</summary>
    public static string LogName(RequestOriginKind kind) => kind switch
    {
        RequestOriginKind.App => "app",
        RequestOriginKind.Web => "web",
        _ => "unknown"
    };

    /// <summary>The phrase used in the Spanish emails ("Esta solicitud se hizo ...").</summary>
    public static string Spanish(RequestOriginKind kind) => kind switch
    {
        RequestOriginKind.App => "desde la app de TRAZO",
        RequestOriginKind.Web => "desde la web",
        _ => "desde un origen desconocido"
    };
}
