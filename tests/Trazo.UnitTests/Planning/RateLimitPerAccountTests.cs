using System.Net;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Signed-in limits count each account on its own, even when several use the same address (a gym network, a family).</summary>
public sealed class RateLimitPerAccountTests() : PlansApiBase(enforced: false)
{
    [Theory]
    [InlineData("/api/v1/account", 30)]
    [InlineData("/api/v1/plans/current", 120)]
    public async Task OneAccountUsingUpItsLimit_DoesNotBlockAnother(string url, int limit)
    {
        var ana = ClientFor(await AddUserAsync("ana@example.com"));
        var eve = ClientFor(await AddUserAsync("eve@example.com"));

        for (var i = 0; i < limit; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, (await ana.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ana.GetAsync(url)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await eve.GetAsync(url)).StatusCode);
    }
}
