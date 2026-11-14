using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trazo.Infrastructure.Settings;

public delegate bool TryParse<T>(string raw, out T value);

/// <summary>
/// Parsing and validation of the operational settings (CORS, rate limits, logging, DietoExpress). The same rules
/// are applied when a superadmin saves a value and when a stored value is read back, so a bad row can never be used.
/// </summary>
public static class OperationalValidator
{
    public const int MaxCorsOrigins = 20;
    public const int MaxOriginLength = 200;
    public const int MaxEndpointLength = 500;
    public const int MinApiKeyLength = 16;
    public const int MaxApiKeyLength = 200;
    public const int MaxLogRetentionDays = 3650;

    public static readonly (int Min, int Max) SetupRange = (10, 600);
    public static readonly (int Min, int Max) SetupTestRange = (3, 60);
    public static readonly (int Min, int Max) SubscriptionRange = (5, 600);

    /// <summary>Trace and None are not offered: Trace would flood the stored logs and None would silence the API completely.</summary>
    public static readonly string[] LogLevels = ["Debug", "Information", "Warning", "Error", "Critical"];

    /// <summary>"https://host[:port]" (or http only for localhost). Returns the canonical lower-case origin.</summary>
    public static bool TryNormalizeOrigin(string? raw, out string origin)
    {
        origin = "";
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxOriginLength) return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return false;
        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath != "/" && uri.AbsolutePath != "")) return false;
        origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }

    /// <summary>A list separated by new lines, commas or semicolons. All entries must be valid; duplicates are removed.</summary>
    public static bool TryParseOrigins(string raw, out IReadOnlyList<string> origins)
    {
        origins = [];
        var result = new List<string>();
        foreach (var part in raw.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryNormalizeOrigin(part, out var origin)) return false;
            if (!result.Contains(origin)) result.Add(origin);
        }
        if (result.Count > MaxCorsOrigins) return false;
        origins = result;
        return true;
    }

    public static string JoinOrigins(IEnumerable<string> origins) => string.Join('\n', origins);

    public static bool TryParseInt(string raw, int min, int max, out int value) =>
        int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    public static bool TryParseLogLevel(string raw, out string level)
    {
        level = LogLevels.FirstOrDefault(x => string.Equals(x, raw.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        return level.Length > 0;
    }

    public static bool TryParseRetention(string raw, out int days) => TryParseInt(raw, 0, MaxLogRetentionDays, out days);

    /// <summary>Absolute https URL (http only for loopback) without credentials, query or fragment. Trailing slashes are removed.</summary>
    public static bool TryParseEndpoint(string raw, out string endpoint)
    {
        endpoint = "";
        var text = raw.Trim();
        if (text.Length is 0 or > MaxEndpointLength || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (!(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        endpoint = text.TrimEnd('/');
        return true;
    }

    public static Dictionary<string, string[]> Validate(OperationsInput? input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input is null) { errors["operations"] = ["Required."]; return errors; }

        if (input.AdditionalCorsOrigins is not null)
        {
            var invalid = input.AdditionalCorsOrigins.Where(x => !TryNormalizeOrigin(x, out _)).ToList();
            if (invalid.Count > 0)
                errors["additionalCorsOrigins"] = [$"Cada origen debe ser https://dominio[:puerto] sin ruta (http solo para localhost). No válido: {Shorten(invalid[0])}"];
            else if (input.AdditionalCorsOrigins.Count > MaxCorsOrigins)
                errors["additionalCorsOrigins"] = [$"Como máximo {MaxCorsOrigins} orígenes."];
        }
        Range(errors, "setupPerMinute", input.SetupPerMinute, SetupRange);
        Range(errors, "setupTestPerMinute", input.SetupTestPerMinute, SetupTestRange);
        Range(errors, "subscriptionPerMinute", input.SubscriptionPerMinute, SubscriptionRange);
        if (!string.IsNullOrWhiteSpace(input.LogLevel) && !TryParseLogLevel(input.LogLevel, out _))
            errors["logLevel"] = [$"Usa uno de: {string.Join(", ", LogLevels)}."];
        if (input.LogRetentionDays is < 0 or > MaxLogRetentionDays)
            errors["logRetentionDays"] = [$"Debe estar entre 0 (conservar siempre) y {MaxLogRetentionDays} días."];
        if (!string.IsNullOrWhiteSpace(input.DietoExpressEndpoint) && !TryParseEndpoint(input.DietoExpressEndpoint, out _))
            errors["dietoExpressEndpoint"] = ["Debe ser una URL https:// absoluta (http solo para localhost), sin usuario, parámetros ni fragmento."];
        if (input.DietoExpressApiKey is { Length: > 0 } key &&
            (key.Length is < MinApiKeyLength or > MaxApiKeyLength || key.Any(char.IsWhiteSpace)))
            errors["dietoExpressApiKey"] = [$"La clave debe tener entre {MinApiKeyLength} y {MaxApiKeyLength} caracteres, sin espacios."];
        return errors;
    }

    private static void Range(Dictionary<string, string[]> errors, string field, int value, (int Min, int Max) range)
    {
        if (value < range.Min || value > range.Max)
            errors[field] = [$"Debe estar entre {range.Min} y {range.Max} peticiones por minuto."];
    }

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}

/// <summary>
/// Resolution order for every operational setting: a valid database value, else a valid environment/appsettings
/// value, else the default. An invalid value is skipped (and reported), so a bad row can never stop the API.
/// </summary>
public static class OperationalResolver
{
    public static T Resolve<T>(
        string key, Func<string, string?> database, Func<string, string?> environment,
        TryParse<T> parse, T fallback, Action<string, string>? onInvalid = null)
    {
        foreach (var (source, read) in new (string, Func<string, string?>)[] { ("database", database), ("environment", environment) })
        {
            var raw = read(key);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (parse(raw, out var value)) return value;
            onInvalid?.Invoke(key, source);
        }
        return fallback;
    }

    public static OperationalConfig Build(
        Func<string, string?> database, Func<string, string?> environment, Action<string, string>? onInvalid = null)
    {
        var origins = Resolve<IReadOnlyList<string>>(SettingKeys.CorsAdditionalOrigins, database, environment,
            OperationalValidator.TryParseOrigins, [], onInvalid);
        var setup = Resolve(SettingKeys.RateLimitSetup, database, environment, Range(OperationalValidator.SetupRange),
            OperationalConfig.DefaultSetupPerMinute, onInvalid);
        var setupTest = Resolve(SettingKeys.RateLimitSetupTest, database, environment, Range(OperationalValidator.SetupTestRange),
            OperationalConfig.DefaultSetupTestPerMinute, onInvalid);
        var subscription = Resolve(SettingKeys.RateLimitSubscription, database, environment, Range(OperationalValidator.SubscriptionRange),
            OperationalConfig.DefaultSubscriptionPerMinute, onInvalid);
        var retention = Resolve<int>(SettingKeys.LogRetentionDays, database, environment,
            OperationalValidator.TryParseRetention, OperationalConfig.DefaultLogRetentionDays, onInvalid);

        // Only a valid database value is layered over the logging configuration; otherwise the environment applies untouched.
        string? logOverride = null;
        var databaseLevel = database(SettingKeys.LogLevelDefault);
        if (!string.IsNullOrWhiteSpace(databaseLevel))
        {
            if (OperationalValidator.TryParseLogLevel(databaseLevel, out var level)) logOverride = level;
            else onInvalid?.Invoke(SettingKeys.LogLevelDefault, "database");
        }
        var effectiveLevel = logOverride ?? Resolve(SettingKeys.LogLevelDefault, _ => null, environment,
            OperationalValidator.TryParseLogLevel, OperationalConfig.DefaultLogLevel, onInvalid);

        return new OperationalConfig(origins, setup, setupTest, subscription, effectiveLevel, logOverride, retention);
    }

    /// <summary>CORS origins from the environment (Cors:AllowedOrigins). They are always allowed, whatever the database says.</summary>
    public static IReadOnlyList<string> EnvironmentOrigins(IConfiguration configuration) =>
        (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
        .Select(x => x.Trim().TrimEnd('/')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static TryParse<int> Range((int Min, int Max) range) =>
        (string raw, out int value) => OperationalValidator.TryParseInt(raw, range.Min, range.Max, out value);
}

/// <summary>
/// Configuration layer that holds values taken from the database, so that framework features that watch
/// <see cref="IConfiguration"/> (such as the log level filters) pick them up without a restart. It is added last, so it
/// wins over the environment; when the database has no value, the key is removed and the environment shows through.
/// </summary>
public sealed class RuntimeOverridesConfigurationSource : IConfigurationSource
{
    public RuntimeOverridesConfigurationProvider Provider { get; } = new();
    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

public sealed class RuntimeOverridesConfigurationProvider : ConfigurationProvider
{
    /// <summary>Category of the audit entries ("who changed what"); they must survive a strict log level.</summary>
    public const string AuditCategory = "Trazo.Admin";

    public void SetLogLevel(string? level)
    {
        var current = Data.TryGetValue(SettingKeys.LogLevelDefault, out var existing) ? existing : null;
        if (string.Equals(current, level, StringComparison.Ordinal)) return;
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (level is not null)
        {
            data[SettingKeys.LogLevelDefault] = level;
            // A strict default level must not silence the audit trail of administrative changes.
            if (level is "Warning" or "Error" or "Critical") data[$"Logging:LogLevel:{AuditCategory}"] = "Information";
        }
        Data = data;
        OnReload();
    }
}

public static class ConfigurationBaseline
{
    /// <summary>The environment/appsettings value of a key, ignoring the database layer.</summary>
    public static string? Get(IConfiguration configuration, string key)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider is RuntimeOverridesConfigurationProvider) continue;
                if (provider.TryGet(key, out var value)) return value;
            }
            return null;
        }
        return configuration[key];
    }
}

/// <summary>Effective operational settings, kept in memory and refreshed from the database after every save and periodically.</summary>
public interface IRuntimeOptions
{
    OperationalConfig Current { get; }
    IReadOnlyList<string> EnvironmentCorsOrigins { get; }
    bool IsCorsOriginAllowed(string origin);
    Task RefreshAsync(CancellationToken ct = default);
}

public sealed class RuntimeOptions : IRuntimeOptions
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RuntimeOptions> _logger;
    private readonly RuntimeOverridesConfigurationProvider? _overrides;
    private readonly HashSet<string> _environmentOrigins;
    private readonly HashSet<string> _reportedInvalid = [];
    private volatile OperationalConfig _current;

    public RuntimeOptions(
        IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RuntimeOptions> logger,
        RuntimeOverridesConfigurationSource? overrides = null)
    {
        _scopes = scopes;
        _logger = logger;
        _overrides = overrides?.Provider;
        EnvironmentCorsOrigins = OperationalResolver.EnvironmentOrigins(configuration);
        _environmentOrigins = new HashSet<string>(EnvironmentCorsOrigins, StringComparer.OrdinalIgnoreCase);
        // Until the first database read, the environment (or defaults) apply: exactly the behaviour before this existed.
        _current = OperationalResolver.Build(_ => null, key => ConfigurationBaseline.Get(configuration, key));
    }

    public OperationalConfig Current => _current;
    public IReadOnlyList<string> EnvironmentCorsOrigins { get; }

    public bool IsCorsOriginAllowed(string origin)
    {
        var trimmed = origin.Trim().TrimEnd('/');
        return _environmentOrigins.Contains(trimmed) ||
               _current.AdditionalCorsOrigins.Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<IRuntimeSettings>();
            var invalid = new HashSet<string>();
            var config = await settings.GetOperationalAsync(ct, (key, source) => invalid.Add($"{source}:{key}"));
            _current = config;
            _overrides?.SetLogLevel(config.LogLevelOverride);
            // Report a bad value once while it stays bad (not every 30 seconds); a fixed value that breaks again is reported again.
            string[] fresh;
            lock (_reportedInvalid)
            {
                fresh = invalid.Where(x => !_reportedInvalid.Contains(x)).ToArray();
                _reportedInvalid.Clear();
                _reportedInvalid.UnionWith(invalid);
            }
            foreach (var item in fresh)
            {
                var separator = item.IndexOf(':');
                _logger.LogWarning(
                    "The {Source} value of setting {Key} is invalid and was ignored; falling back to the next source or the default.",
                    item[..separator], item[(separator + 1)..]);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Keeping the last good values is always safe; the settings are never needed to start or to sign in.
            _logger.LogWarning(exception, "The runtime settings could not be read from the database; keeping the previous values.");
        }
    }

}

/// <summary>Reloads <see cref="IRuntimeOptions"/> every 30 seconds (and once at startup, after the migrations).</summary>
public sealed class RuntimeOptionsRefresher(IRuntimeOptions options) : Microsoft.Extensions.Hosting.BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do { await options.RefreshAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
