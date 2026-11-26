namespace Trazo.Application.Planning;

/// <summary>
/// What the app making the request says it can show (<c>X-Client-Capabilities</c>, comma separated). An app that says nothing
/// is served plain sets, so one published before a feature existed keeps working.
/// </summary>
public sealed class ClientCapabilities
{
    public const string Header = "X-Client-Capabilities";
    public const string TechniquesName = "techniques-1";

    /// <summary>Techniques, superset rounds and the segments of logged sets.</summary>
    public bool Techniques { get; private set; }

    public void Read(string? header) =>
        Techniques = (header ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(TechniquesName, StringComparer.OrdinalIgnoreCase);
}
