using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Trazo.Domain.Growth;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Growth;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

public sealed class PremiumGrantTests : IDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    internal static async Task<TrazoUser> AddUserAsync(TrazoDbContext db, string email)
    {
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = "x", Role = "user",
            EmailVerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private PremiumGrantService Grants(TrazoDbContext db, DateTimeOffset? now = null) =>
        new(db, new FixedClock(now ?? Now), NullLogger<PremiumGrantService>.Instance);

    [Fact]
    public async Task Grant_SameReasonTwice_IsRecordedOnce()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "a@example.com");
        var service = Grants(db);

        var first = await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "ref-1", 30), default);
        var second = await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "ref-1", 30), default);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Grant.Id, second.Grant.Id);
        Assert.Equal(1, await db.PremiumGrants.CountAsync());
    }

    [Fact]
    public async Task Grant_ConcurrentCallsForTheSameReason_CreateASingleGrant()
    {
        Guid userId;
        using (var seed = _fixture.NewContext()) userId = (await AddUserAsync(seed, "race@example.com")).Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var db = _fixture.NewContext();
            return await Grants(db).GrantAsync(new GrantRequest(userId, PremiumGrantKinds.ReferralReward, "ref-race", 30), default);
        }));

        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Single(results.Select(r => r.Grant.Id).Distinct());
        using var check = _fixture.NewContext();
        Assert.Equal(1, await check.PremiumGrants.CountAsync());
    }

    [Fact]
    public async Task Grants_AreChained_NotOverlapped()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "chain@example.com");
        var service = Grants(db);

        var a = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "r1", 30), default)).Grant;
        var b = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "r2", 30), default)).Grant;

        Assert.Equal(Now, a.StartsAt);
        Assert.Equal(Now.AddDays(30), a.EndsAt);
        Assert.Equal(a.EndsAt, b.StartsAt);
        Assert.Equal(Now.AddDays(60), b.EndsAt);
        Assert.Equal(Now.AddDays(60), await service.PromoEndAsync(user.Id, default));
    }

    [Fact]
    public async Task Access_ExistsInsideTheWindow_AndEndsAfterIt()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "window@example.com");
        await Grants(db).GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.SignupTrial, user.Id.ToString("N"), 30), default);

        Assert.True(await Grants(db, Now.AddDays(29)).HasPromoAsync(user.Id, default));
        Assert.False(await Grants(db, Now.AddDays(30)).HasPromoAsync(user.Id, default));
        Assert.False(await Grants(db, Now.AddDays(-1)).HasPromoAsync(user.Id, default));
    }

    [Fact]
    public async Task PendingAndVoidedGrants_GiveNoAccess()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "pending@example.com");
        var service = Grants(db);
        var pending = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "p", 30, Status: PremiumGrantStatuses.Pending), default)).Grant;
        Assert.False(await service.HasPromoAsync(user.Id, default));

        await service.ActivatePendingAsync(pending.Id, default);
        Assert.True(await service.HasPromoAsync(user.Id, default));

        Assert.Equal(VoidOutcome.Voided, await service.VoidAsync(pending.Id, "refund", "system", default));
        Assert.False(await service.HasPromoAsync(user.Id, default));
        Assert.Equal(VoidOutcome.AlreadyVoided, await service.VoidAsync(pending.Id, "again", "system", default));
    }

    [Fact]
    public async Task Void_ClosesTheGapInTheChain()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "gap@example.com");
        var service = Grants(db);
        var a = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "g1", 30), default)).Grant;
        var b = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "g2", 30), default)).Grant;

        await service.VoidAsync(a.Id, "refund", "system", default);

        await db.Entry(b).ReloadAsync();
        Assert.Equal(Now, b.StartsAt);
        Assert.Equal(Now.AddDays(30), b.EndsAt);
    }

    [Fact]
    public async Task Void_ADeferredGrant_IsNotReversible()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "deferred@example.com");
        var service = Grants(db);
        var grant = (await service.GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "d", 30, Status: PremiumGrantStatuses.Pending), default)).Grant;
        grant.Status = PremiumGrantStatuses.Deferred;
        await db.SaveChangesAsync();

        Assert.Equal(VoidOutcome.NotReversible, await service.VoidAsync(grant.Id, "refund", "system", default));
        await db.Entry(grant).ReloadAsync();
        Assert.Equal(PremiumGrantStatuses.Deferred, grant.Status);
    }

    [Fact]
    public async Task PermanentAdminGrant_GivesAccessWithoutAnExpiry_AndRequiresAReason()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "permanent@example.com");
        var service = Grants(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GrantAsync(
            new GrantRequest(user.Id, PremiumGrantKinds.AdminGrant, "missing-reason", 0, IsPermanent: true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GrantAsync(
            new GrantRequest(user.Id, PremiumGrantKinds.ReferralReward, "permanent-referral", 0, IsPermanent: true, Reason: "Not allowed"), default));

        var result = await service.GrantAsync(new GrantRequest(
            user.Id, PremiumGrantKinds.AdminGrant, "permanent-admin", 0, IsPermanent: true, Reason: "Internal test account"), default);

        Assert.True(result.Grant.IsPermanent);
        Assert.Null(result.Grant.EndsAt);
        Assert.Equal(0, result.Grant.Days);
        Assert.Equal("Internal test account", result.Grant.GrantReason);
        Assert.True(await service.HasPromoAsync(user.Id, default));
        Assert.Null(await service.PromoEndAsync(user.Id, default));
    }

    [Fact]
    public async Task RevokingPermanentAdminGrant_RestartsFiniteGrantWindows_AndRemovesOnlyItsAccess()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "permanent-chain@example.com");
        var service = Grants(db);
        var permanent = (await service.GrantAsync(new GrantRequest(
            user.Id, PremiumGrantKinds.AdminGrant, "permanent-overlay", 0, IsPermanent: true, Reason: "Partner access"), default)).Grant;
        var reward = (await service.GrantAsync(new GrantRequest(
            user.Id, PremiumGrantKinds.ReferralReward, "after-permanent", 20), default)).Grant;

        Assert.True(await service.HasPromoAsync(user.Id, default));
        Assert.Equal(VoidOutcome.Voided, await service.VoidAsync(permanent.Id, "Access period finished", "admin-id", default));

        await db.Entry(reward).ReloadAsync();
        Assert.Equal(Now, reward.StartsAt);
        Assert.Equal(Now.AddDays(20), reward.EndsAt);
        Assert.True(await service.HasPromoAsync(user.Id, default));
        Assert.Equal(Now.AddDays(20), await service.PromoEndAsync(user.Id, default));
    }

    [Fact]
    public async Task Grant_RequiresPositiveDays()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "zero@example.com");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Grants(db).GrantAsync(new GrantRequest(user.Id, PremiumGrantKinds.AdminGrant, "z", 0), default));
    }

    private (SignupTrialService Service, TrazoDbContext Db, SettingsWriter Writer) Trial()
    {
        var (db, _, runtime, writer) = _fixture.NewServices();
        var grants = Grants(db);
        var events = new GrowthEventRecorder(db, new FixedClock(Now), NullLogger<GrowthEventRecorder>.Instance);
        return (new SignupTrialService(runtime, grants, events), db, writer);
    }

    [Fact]
    public async Task SignupTrial_DefaultsToThirtyDays_AndIsGrantedOnce()
    {
        var (service, db, _) = Trial();
        using var _ = db;
        var user = await AddUserAsync(db, "new@example.com");

        var first = await service.ActivatedAsync(user.Id, default);
        var second = await service.ActivatedAsync(user.Id, default);

        Assert.NotNull(first);
        Assert.Equal(30, first!.Days);
        Assert.Equal(first.Id, second!.Id);
        Assert.Equal(1, await db.PremiumGrants.CountAsync());
        Assert.Equal(1, await db.GrowthEvents.CountAsync(x => x.Name == GrowthEventNames.SignupTrialGranted));
        Assert.Equal(1, await db.GrowthEvents.CountAsync(x => x.Name == GrowthEventNames.EmailVerified));
    }

    [Fact]
    public async Task SignupTrial_UsesTheDaysConfiguredBySuperadmin_AndCanBeSwitchedOff()
    {
        var (service, db, writer) = Trial();
        using var _ = db;
        Assert.Empty(await writer.UpdatePromotionsAsync(new PromotionsConfig(true, 45, true, 30), default));
        var a = await AddUserAsync(db, "a@example.com");
        Assert.Equal(45, (await service.ActivatedAsync(a.Id, default))!.Days);

        Assert.Empty(await writer.UpdatePromotionsAsync(new PromotionsConfig(false, 45, true, 30), default));
        var b = await AddUserAsync(db, "b@example.com");
        Assert.Null(await service.ActivatedAsync(b.Id, default));
        Assert.Equal(1, await db.PremiumGrants.CountAsync()); // the first account keeps what it was given
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(366, 30)]
    [InlineData(30, 0)]
    public async Task Promotions_RejectOutOfRangeDays(int trialDays, int rewardDays)
    {
        var (_, db, writer) = Trial();
        using var _ = db;
        var errors = await writer.UpdatePromotionsAsync(new PromotionsConfig(true, trialDays, true, rewardDays), default);
        Assert.NotEmpty(errors);
        var runtime = new RuntimeSettings(db, new ConfigurationBuilder().Build(), new SecretProtector(_fixture.Protection));
        Assert.Equal(PromotionsConfig.Default, await runtime.GetPromotionsAsync());
    }

    [Fact]
    public async Task GrowthEvents_WithTheSameDedupeKey_AreRecordedOnce()
    {
        using var db = _fixture.NewContext();
        var user = await AddUserAsync(db, "ev@example.com");
        var events = new GrowthEventRecorder(db, new FixedClock(Now), NullLogger<GrowthEventRecorder>.Instance);

        Assert.True(await events.RecordAsync(GrowthEventNames.FirstPurchase, user.Id, new GrowthEventInfo(DedupeKey: "k"), default));
        Assert.False(await events.RecordAsync(GrowthEventNames.FirstPurchase, user.Id, new GrowthEventInfo(DedupeKey: "k"), default));
        Assert.Equal(1, await db.GrowthEvents.CountAsync());
    }
}
