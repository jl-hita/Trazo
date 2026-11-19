using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Trazo.UnitTests;

/// <summary>
/// A fact that runs against everything except PostgreSQL scenarios when <c>TRAZO_TEST_POSTGRES</c> is not configured. A test class whose
/// name starts with "Postgres" is reported as SKIPPED (with the reason) when the variable is empty, so a machine without PostgreSQL gets a
/// green, honest suite. When the variable IS set the test runs normally: a connection or migration error fails it, nothing is hidden.
/// </summary>
[XunitTestCaseDiscoverer("Trazo.UnitTests.ScenarioFactDiscoverer", "Trazo.UnitTests")]
public sealed class ScenarioFactAttribute : FactAttribute;

public static class PostgresSettings
{
    public const string Variable = "TRAZO_TEST_POSTGRES";

    /// <summary>The connection string, or an empty string when PostgreSQL is not configured here.</summary>
    public static string ConnectionString => Environment.GetEnvironmentVariable(Variable) ?? string.Empty;

    public static bool Configured => !string.IsNullOrWhiteSpace(ConnectionString);
}

public sealed class ScenarioFactDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        var className = testMethod.TestClass.Class.Name;
        var isPostgres = className.Split('.').Last().StartsWith("Postgres", StringComparison.Ordinal);
        var display = discoveryOptions.MethodDisplayOrDefault();
        var displayOptions = discoveryOptions.MethodDisplayOptionsOrDefault();
        if (isPostgres && !PostgresSettings.Configured)
            yield return new XunitSkippedDataRowTestCase(
                diagnosticMessageSink, display, displayOptions, testMethod, $"{PostgresSettings.Variable} is not set: PostgreSQL scenarios are skipped.");
        else
            yield return new XunitTestCase(diagnosticMessageSink, display, displayOptions, testMethod);
    }
}
