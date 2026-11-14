using Xunit;
using Trazo.Api.Security;
using Trazo.Infrastructure.Settings;

namespace Trazo.UnitTests;

public sealed class SignInProtectionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void FiveFailuresBlockOnlyThatAddressForThatAccount()
    {
        var clock = new Clock();
        var throttle = new LoginAttemptThrottle(clock);
        var victim = Guid.NewGuid();
        var other = Guid.NewGuid();

        for (var i = 0; i < LoginAttemptThrottle.MaximumFailures - 1; i++) throttle.RegisterFailure(victim, "203.0.113.7");
        Assert.False(throttle.IsBlocked(victim, "203.0.113.7"));
        throttle.RegisterFailure(victim, "203.0.113.7");

        Assert.True(throttle.IsBlocked(victim, "203.0.113.7"));
        Assert.False(throttle.IsBlocked(victim, "198.51.100.9")); // the owner, from home, is not locked out
        Assert.False(throttle.IsBlocked(other, "203.0.113.7"));
    }

    [Fact]
    public void TheBlockEndsWithTheWindowAndWithASuccessfulSignIn()
    {
        var clock = new Clock();
        var throttle = new LoginAttemptThrottle(clock);
        var user = Guid.NewGuid();
        for (var i = 0; i < LoginAttemptThrottle.MaximumFailures; i++) throttle.RegisterFailure(user, "a");

        clock.Now += LoginAttemptThrottle.Window + TimeSpan.FromSeconds(1);
        Assert.False(throttle.IsBlocked(user, "a"));

        for (var i = 0; i < LoginAttemptThrottle.MaximumFailures; i++) throttle.RegisterFailure(user, "a");
        throttle.Reset(user, "a");
        Assert.False(throttle.IsBlocked(user, "a"));
    }

    [Fact]
    public void AGoogleNonceIsAcceptedOnce()
    {
        var store = new GoogleNonceStore(new Clock());
        var nonce = store.Issue();

        Assert.True(store.Accept(nonce, required: true));
        Assert.False(store.Accept(nonce, required: true));
        Assert.False(store.Accept("never-issued", required: false));
    }

    [Fact]
    public void AGoogleNonceExpires()
    {
        var clock = new Clock();
        var store = new GoogleNonceStore(clock);
        var nonce = store.Issue();
        clock.Now += GoogleNonceStore.Lifetime + TimeSpan.FromSeconds(1);

        Assert.False(store.Accept(nonce, required: false));
    }

    [Fact]
    public void ATokenWithoutNonceIsAcceptedOnlyWhileNoncesAreOptional()
    {
        var store = new GoogleNonceStore(new Clock());

        Assert.True(store.Accept(null, required: false));
        Assert.False(store.Accept(null, required: true));
        Assert.False(store.Accept("", required: true));
    }

    [Theory]
    [InlineData("https://trazo.example.com", true)]
    [InlineData("http://localhost:4200", true)]
    [InlineData("trazo://account", false)]
    [InlineData("http://trazo.example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    public void EmailLinksMustBeWebLinks(string actionBaseUrl, bool accepted)
    {
        var errors = SettingsValidator.ValidateEmail(
            new EmailConfig("smtp.example.com", 587, true, null, null, "no-reply@example.com", actionBaseUrl));

        Assert.Equal(accepted, !errors.ContainsKey("email.actionBaseUrl"));
    }
}
