using EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Saving an LGU's PayMongo keys, and registering its webhook for it.
///
/// <para>
/// The office pastes one secret key. Registering the webhook is StallTrack's job, because a signing secret is not something
/// an LGU clerk should have to know exists - and because PayMongo's own documentation recommends provisioning per-merchant
/// webhooks through the API rather than by hand.
/// </para>
///
/// <para>
/// The rule that governs the whole handler: THE KEY IS SAVED FIRST. Registering a webhook needs PayMongo to answer; storing
/// a key does not. An office must never lose the key it just pasted because a network call failed a moment later, so every
/// failure below is reported as "saved, but" and none of them rolls anything back.
/// </para>
/// </summary>
public class SetMunicipalityPaymentCredentialsProvisioningTests
{
    private const string Secret = "sk_live_madrid";
    private const string WebhookUrl = "https://api.stalltrack.site/api/onlinepayments/webhook/madrid";
    private static readonly DateTime Now = new(2026, 8, 19, 6, 0, 0, DateTimeKind.Utc);

    private static async Task<(SetMunicipalityPaymentCredentialsCommandHandler handler, Municipality lgu, AppDbContext ctx)> BuildAsync(
        Result<PayMongoWebhookRegistration>? provisioning = null, bool urlBuilderThrows = false)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var lgu = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, "madrid");

        await using (var seed = new AppDbContext(options))
        {
            seed.Municipalities.Add(lgu);
            await seed.SaveChangesAsync();
        }

        var ctx = new AppDbContext(options);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.MunicipalityId).Returns(lgu.Id);
        user.SetupGet(u => u.Username).Returns("head");

        var protector = new Mock<ICredentialProtector>();
        protector.Setup(p => p.Protect(It.IsAny<string>())).Returns((string plain) => "enc:" + plain);
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
                 .Returns((string enc) => enc.StartsWith("enc:") ? enc[4..] : enc);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("madrid");

        var verifier = new Mock<IPayMongoAccountVerifier>();
        verifier.Setup(v => v.EnsureWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(provisioning ?? Result<PayMongoWebhookRegistration>.Success(
                    new PayMongoWebhookRegistration("hook_1", "whsk_new", AlreadyExisted: false, WasReEnabled: false)));

        var urls = new Mock<IOnlinePaymentUrlBuilder>();
        if (urlBuilderThrows)
            urls.Setup(u => u.BuildWebhookUrl(It.IsAny<string>())).Throws(new InvalidOperationException("no public address"));
        else
            urls.Setup(u => u.BuildWebhookUrl(It.IsAny<string>())).Returns(WebhookUrl);

        var handler = new SetMunicipalityPaymentCredentialsCommandHandler(
            ctx, user.Object, protector.Object, Mock.Of<IEemoCacheInvalidator>(), tenant.Object,
            verifier.Object, urls.Object, new FixedClock(Now));

        return (handler, lgu, ctx);
    }

    private static async Task<Municipality> Reload(AppDbContext ctx, Guid id) =>
        await ctx.Municipalities.AsNoTracking().FirstAsync(m => m.Id == id);

    [Fact]
    public async Task OneSecretKeyIsEnough_TheWebhookIsRegisteredAndItsSecretKept()
    {
        var (handler, lgu, ctx) = await BuildAsync();
        await using var _ctx = ctx;

        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Saved);
        Assert.True(result.Value.WebhookRegistered);

        var saved = await Reload(ctx, lgu.Id);
        Assert.True(saved.HasOwnPayMongoAccount);
        Assert.True(saved.HasPayMongoWebhookSecret);
        Assert.Equal("hook_1", saved.PayMongoWebhookId);
        Assert.Equal(Now, saved.PayMongoLastVerifiedAtUtc);
    }

    [Fact]
    public async Task WhenProvisioningFAILSTheKeyIsStillSaved()
    {
        // The rule the whole handler is built around. PayMongo being unreachable must not cost the office the key it just
        // pasted - it is usable for taking payments either way.
        var (handler, lgu, ctx) = await BuildAsync(
            Result<PayMongoWebhookRegistration>.Failure("Could not reach PayMongo to register the webhook.", ResultStatus.UpstreamFailed));
        await using var _ctx = ctx;

        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Saved);
        Assert.False(result.Value.WebhookRegistered);

        var saved = await Reload(ctx, lgu.Id);
        Assert.True(saved.HasOwnPayMongoAccount);          // the key survived
        Assert.False(saved.HasPayMongoWebhookSecret);
        Assert.Null(saved.PayMongoWebhookId);
    }

    [Fact]
    public async Task AMisconfiguredPublicAddressAlsoLeavesTheKeySaved()
    {
        var (handler, lgu, ctx) = await BuildAsync(urlBuilderThrows: true);
        await using var _ctx = ctx;

        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        Assert.True(result.Value!.Saved);
        Assert.False(result.Value.WebhookRegistered);
        Assert.Contains("public address", result.Value.Message);
        Assert.True((await Reload(ctx, lgu.Id)).HasOwnPayMongoAccount);
    }

    [Fact]
    public async Task ASecretTheOFFICETypedIsNeverOverwrittenByProvisioning()
    {
        // An office that pasted its own signing secret meant it. Replacing it with one this system happened to obtain would
        // be overruling them about their own account.
        var (handler, lgu, ctx) = await BuildAsync();
        await using var _ctx = ctx;

        await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, "whsk_typed_by_office"), CancellationToken.None);

        var saved = await Reload(ctx, lgu.Id);
        Assert.Equal("enc:whsk_typed_by_office", saved.PayMongoWebhookSecretEnc);
    }

    [Fact]
    public async Task AnALREADYRegisteredWebhookIsReusedRatherThanDuplicated()
    {
        // Found rather than created, so PayMongo does not end up holding two webhooks for the same address. No secret comes
        // back in that case, and the stored one must not be blanked because of it.
        var (handler, lgu, ctx) = await BuildAsync(Result<PayMongoWebhookRegistration>.Success(
            new PayMongoWebhookRegistration("hook_existing", null, AlreadyExisted: true, WasReEnabled: false)));
        await using var _ctx = ctx;

        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        var saved = await Reload(ctx, lgu.Id);
        Assert.Equal("hook_existing", saved.PayMongoWebhookId);
        Assert.False(saved.HasPayMongoWebhookSecret);                 // nothing was revealed, so nothing was invented
        Assert.False(result.Value!.WebhookRegistered);                // cannot authenticate yet, and says so
        Assert.Contains("signing secret", result.Value.Message);
    }

    [Fact]
    public async Task ADisabledWebhookThatWasSwitchedBackOnIsSaidSo()
    {
        var (handler, _, ctx) = await BuildAsync(Result<PayMongoWebhookRegistration>.Success(
            new PayMongoWebhookRegistration("hook_1", "whsk_new", AlreadyExisted: true, WasReEnabled: true)));
        await using var _ctx = ctx;

        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        Assert.True(result.Value!.WebhookRegistered);
        Assert.Contains("re-enabled", result.Value.Message);
    }

    [Fact]
    public async Task RESAVINGTheSameKeyKeepsTheSigningSecretAlreadyStored()
    {
        // The bug this guards. The signing secret field is optional now, because provisioning fills it in - so a Head who
        // re-saves their key without retyping it would have wiped a working secret and had no idea why notifications
        // stopped being believed. Nothing on the screen would have said so either: the field is blank by design.
        //
        // The SEQUENCE is what makes this test mean anything. The second save must find the webhook already registered and
        // be told no new secret, which is the realistic case - PayMongo reveals a secret when a webhook is created. With a
        // mock that hands back a fresh secret every time, a wiped secret is silently rewritten and the test proves nothing.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var lgu = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, "madrid");
        await using (var seed = new AppDbContext(options))
        {
            seed.Municipalities.Add(lgu);
            await seed.SaveChangesAsync();
        }

        await using var ctx = new AppDbContext(options);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.MunicipalityId).Returns(lgu.Id);
        user.SetupGet(u => u.Username).Returns("head");

        var protector = new Mock<ICredentialProtector>();
        protector.Setup(p => p.Protect(It.IsAny<string>())).Returns((string plain) => "enc:" + plain);
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
                 .Returns((string enc) => enc.StartsWith("enc:") ? enc[4..] : enc);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("madrid");

        var verifier = new Mock<IPayMongoAccountVerifier>();
        verifier.SetupSequence(v => v.EnsureWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<PayMongoWebhookRegistration>.Success(
                    new PayMongoWebhookRegistration("hook_1", "whsk_created", AlreadyExisted: false, WasReEnabled: false)))
                .ReturnsAsync(Result<PayMongoWebhookRegistration>.Success(
                    new PayMongoWebhookRegistration("hook_1", null, AlreadyExisted: true, WasReEnabled: false)));

        var urls = new Mock<IOnlinePaymentUrlBuilder>();
        urls.Setup(u => u.BuildWebhookUrl(It.IsAny<string>())).Returns(WebhookUrl);

        var handler = new SetMunicipalityPaymentCredentialsCommandHandler(
            ctx, user.Object, protector.Object, Mock.Of<IEemoCacheInvalidator>(), tenant.Object,
            verifier.Object, urls.Object, new FixedClock(Now));

        await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);
        Assert.Equal("enc:whsk_created", (await Reload(ctx, lgu.Id)).PayMongoWebhookSecretEnc);

        // Same key, signing secret field blank, and nothing new revealed.
        await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        Assert.Equal("enc:whsk_created", (await Reload(ctx, lgu.Id)).PayMongoWebhookSecretEnc);
    }

    [Fact]
    public async Task POINTINGATADIFFERENTAccountDropsTheOldWebhookAndItsSecret()
    {
        // The other direction, and it matters just as much: a signing secret belongs to ONE account's webhook. Carrying it
        // to a different account would let the screen report a connection that cannot be authenticated, and the hook id and
        // verification stamp would describe an account this LGU no longer uses.
        var (handler, lgu, ctx) = await BuildAsync();
        await using var _ctx = ctx;

        await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);

        // A different account, and provisioning cannot be reached this time - so nothing new replaces what was there.
        var stale = await Reload(ctx, lgu.Id);
        Assert.True(stale.HasPayMongoWebhookSecret);

        var (handler2, lgu2, ctx2) = await BuildAsync(
            Result<PayMongoWebhookRegistration>.Failure("unreachable", ResultStatus.UpstreamFailed));
        await using var _ctx2 = ctx2;

        await handler2.Handle(new SetMunicipalityPaymentCredentialsCommand("sk_live_a_different_account", null, null), CancellationToken.None);

        var after = await Reload(ctx2, lgu2.Id);
        Assert.True(after.HasOwnPayMongoAccount);        // the new key is stored
        Assert.False(after.HasPayMongoWebhookSecret);    // the old account's secret is not kept
        Assert.Null(after.PayMongoWebhookId);
        Assert.Null(after.PayMongoLastVerifiedAtUtc);
    }

    [Fact]
    public async Task ClearingTheKeyRemovesTheWebhookItDescribed()
    {
        // The webhook id and the verification stamp describe an account this LGU no longer uses; leaving them would report a
        // connection that is not there.
        var (handler, lgu, ctx) = await BuildAsync();
        await using var _ctx = ctx;

        await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(Secret, null, null), CancellationToken.None);
        var result = await handler.Handle(new SetMunicipalityPaymentCredentialsCommand(null, null, null), CancellationToken.None);

        Assert.True(result.Value!.Saved);

        var saved = await Reload(ctx, lgu.Id);
        Assert.False(saved.HasOwnPayMongoAccount);
        Assert.Null(saved.PayMongoWebhookId);
        Assert.Null(saved.PayMongoLastVerifiedAtUtc);
    }
}
