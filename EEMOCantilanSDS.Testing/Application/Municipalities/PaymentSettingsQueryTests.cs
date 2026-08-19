using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What an office is told about its own online-payment connection.
///
/// <para>
/// The webhook signing secret stopped being described as optional, because the two halves do different jobs: the secret key
/// decides WHERE money settles, and the signing secret decides whether PayMongo's notifications can be believed. An LGU
/// with only the first can take payments that then wait for the payor to come back to the portal - and PayMongo's own
/// documentation is explicit that missed events are never re-sent.
/// </para>
///
/// <para>
/// So the screen reports three states rather than two, shows the office's OWN webhook address, and reads Live or Test from
/// the key's own prefix. Nothing new is stored for any of it, so there is no column to keep in step.
/// </para>
/// </summary>
public class PaymentSettingsQueryTests
{
    private static DbContextOptions<AppDbContext> NewDb() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    /// <summary>Seeds one LGU and returns a handler reading as one of its users.</summary>
    private static async Task<(GetMunicipalityPaymentSettingsQueryHandler handler, Municipality lgu, AppDbContext ctx)> BuildAsync(
        bool isDefault = false, string? secretPlain = null, bool withWebhook = false, bool protectorThrows = false)
    {
        var options = NewDb();

        var lgu = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active,
            "madrid", isDefault: isDefault);

        if (secretPlain is not null)
            lgu.SetPayMongoCredentials("enc:" + secretPlain, null, withWebhook ? "enc:whsk_x" : null, "head");

        await using (var seed = new AppDbContext(options))
        {
            seed.Municipalities.Add(lgu);
            await seed.SaveChangesAsync();
        }

        var ctx = new AppDbContext(options);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.MunicipalityId).Returns(lgu.Id);

        var protector = new Mock<ICredentialProtector>();
        if (protectorThrows)
            protector.Setup(p => p.Unprotect(It.IsAny<string>())).Throws(new InvalidOperationException("bad key"));
        else
            protector.Setup(p => p.Unprotect(It.IsAny<string>()))
                     .Returns((string enc) => enc.StartsWith("enc:") ? enc[4..] : enc);

        // The builder is what composes the webhook address now, so the test states the address it is expected to produce
        // rather than reimplementing the shape here.
        var urls = new Mock<IOnlinePaymentUrlBuilder>();
        urls.Setup(u => u.BuildWebhookUrl(It.IsAny<string>()))
            .Returns((string code) => $"https://api.stalltrack.site/api/onlinepayments/webhook/{code}");

        return (new GetMunicipalityPaymentSettingsQueryHandler(ctx, user.Object, protector.Object, urls.Object), lgu, ctx);
    }

    private static async Task<PaymentSettingsDto> Read(GetMunicipalityPaymentSettingsQueryHandler handler)
    {
        var result = await handler.Handle(new GetMunicipalityPaymentSettingsQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task AnAccountWithNoSigningSecretIsReportedAsSuch()
    {
        // The distinction the screen now makes. Money would settle correctly; nothing would confirm it by itself.
        var (handler, _, ctx) = await BuildAsync(secretPlain: "sk_live_abc", withWebhook: false);
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.True(dto.HasOwnAccount);
        Assert.True(dto.CanAcceptOnlinePayments);
        Assert.False(dto.HasWebhookSecret);
    }

    [Fact]
    public async Task BothHalvesConfiguredIsReportedAsSuch()
    {
        var (handler, _, ctx) = await BuildAsync(secretPlain: "sk_live_abc", withWebhook: true);
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.True(dto.HasOwnAccount);
        Assert.True(dto.HasWebhookSecret);
    }

    [Fact]
    public async Task TheWebhookAddressIsPERLGU()
    {
        // The one thing that must not be generic. The tenant-less endpoint verifies against the platform configuration,
        // which is the DEFAULT municipality's secret, so an office pointed at it would have every notification refused.
        var (handler, lgu, ctx) = await BuildAsync(secretPlain: "sk_live_abc");
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.Equal($"https://api.stalltrack.site/api/onlinepayments/webhook/{lgu.TenantCode}", dto.WebhookUrl);
        Assert.EndsWith("/webhook/" + lgu.TenantCode, dto.WebhookUrl);
        Assert.StartsWith("https://", dto.WebhookUrl);      // PayMongo will not call an http address
    }

    [Theory]
    [InlineData("sk_live_abc", "Live")]
    [InlineData("sk_test_abc", "Test")]
    public async Task TheModeIsReadFromTheKeysOwnPrefix(string secret, string expected)
    {
        // Read, not stored, so there is no column to drift out of step with the key it describes - and an office that
        // pasted a test key into a live portal can see that it did.
        var (handler, _, ctx) = await BuildAsync(secretPlain: secret);
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.Equal(expected, dto.Mode);
    }

    [Fact]
    public async Task AnUnreadableKeyDoesNotBreakTheScreen()
    {
        // A label on a settings page must never be the reason the page cannot load.
        var (handler, _, ctx) = await BuildAsync(secretPlain: "sk_live_abc", protectorThrows: true);
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.Null(dto.Mode);
        Assert.True(dto.HasOwnAccount);
    }

    [Fact]
    public async Task TheDEFAULTMunicipalityStillReadsAsAbleToAcceptPayments()
    {
        // It has no account of its own because the platform configuration IS its account. Unchanged behaviour, asserted so
        // the new fields cannot quietly take it away.
        var (handler, _, ctx) = await BuildAsync(isDefault: true, secretPlain: null);
        await using var _ctx = ctx;

        var dto = await Read(handler);

        Assert.False(dto.HasOwnAccount);
        Assert.True(dto.CanAcceptOnlinePayments);
        Assert.Null(dto.Mode);
    }
}
