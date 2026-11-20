using Trazo.Infrastructure.Analytics;
using Xunit;

namespace Trazo.UnitTests.Analytics;

public sealed class IntelligencePeriodTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void DefaultsToThePreviousNinetyDays()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 30, 0, TimeSpan.Zero);
        var period = IntelligencePeriod.Resolve(null, null, new FixedTimeProvider(now));

        Assert.Equal(now.AddDays(-90), period.From);
        Assert.Equal(now, period.To);
    }

    [Fact]
    public void KeepsAnExplicitValidPeriodUnchanged()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var period = IntelligencePeriod.Resolve(from, to, new FixedTimeProvider(to.AddDays(10)));

        Assert.Equal(from, period.From);
        Assert.Equal(to, period.To);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(732)]
    public void RejectsReversedOrOverlongPeriods(int days)
    {
        var to = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var from = days == 1 ? to.AddDays(1) : to.AddDays(-days);

        Assert.Throws<ArgumentException>(() =>
            IntelligencePeriod.Resolve(from, to, new FixedTimeProvider(to)));
    }

    [Fact]
    public void AllowsTheMaximumTwoYearWindow()
    {
        var to = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var from = to.AddDays(-731);

        var period = IntelligencePeriod.Resolve(from, to, new FixedTimeProvider(to));

        Assert.Equal(TimeSpan.FromDays(731), period.To - period.From);
    }
}
