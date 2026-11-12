using Xunit;

namespace Trazo.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void DomainAssembly_IsAvailable()
    {
        Assert.NotNull(typeof(Trazo.Domain.DomainAssemblyMarker).Assembly);
    }
}
