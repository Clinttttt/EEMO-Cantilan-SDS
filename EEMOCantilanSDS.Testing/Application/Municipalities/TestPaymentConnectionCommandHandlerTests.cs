using EEMOCantilanSDS.Application.Command.Municipalities.TestPaymentConnection;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Testing an LGU's PayMongo connection, and what that is allowed to record.
///
/// <para>
/// The office needs to know two different things at two different moments: before saving, whether the key it just pasted is
/// right; and afterwards, whether the connection it saved still works. So the key in the box is tested when there is one,
/// and the stored key otherwise.
/// </para>
///
/// <para>
/// What it records is the careful part. A success against the office's OWN key stamps the verification. A failure records
/// nothing - overwriting the last time things were known to work would delete the most useful fact on the screen for
/// whoever is diagnosing the problem. And a successful test of a key that has not been saved proves the key works, not that
/// this office's connection does, so it stamps nothing either.
/// </para>
/// </summary>
public class TestPaymentConnectionCommandHandlerTests
{
    private const string StoredPlain = "sk_live_stored";
    private static readonly DateTime Now = new(2026, 8, 19, 3, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = new(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc);

    private static async Task<(TestPaymentConnectionCommandHandler handler, Municipality lgu, AppDbContext ctx)> BuildAsync(
        bool verifierAccepts, bool hasStoredKey = true, DateTime? alreadyVerifiedAt = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        var lgu = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, "madrid");
        if (hasStoredKey) lgu.SetPayMongoCredentials("enc:" + StoredPlain, null, null, "head");
        if (alreadyVerifiedAt is { } at) lgu.RecordPayMongoVerified(at, "head");

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
        protector.Setup(p => p.Unprotect(It.IsAny<string>()))
                 .Returns((string enc) => enc.StartsWith("enc:") ? enc[4..] : enc);

        var verifier = new Mock<IPayMongoAccountVerifier>();
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(verifierAccepts
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure("PayMongo did not accept this secret key.", ResultStatus.Invalid));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .Returns(async (CancellationToken t) => await ctx.SaveChangesAsync(t));

        var handler = new TestPaymentConnectionCommandHandler(
            ctx, user.Object, protector.Object, verifier.Object, uow.Object, new FixedClock(Now));

        return (handler, lgu, ctx);
    }

    [Fact]
    public async Task ASuccessAgainstTheOfficesOwnKeyIsRecorded()
    {
        var (handler, lgu, ctx) = await BuildAsync(verifierAccepts: true);
        await using var _ctx = ctx;

        var result = await handler.Handle(new TestPaymentConnectionCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Ok);
        Assert.Equal(Now, result.Value.VerifiedAtUtc);

        // Read back from the store rather than from the seeded instance: the handler works on the row IT loaded, so
        // asserting on the object this test happens to hold would prove nothing about what was saved.
        Assert.Equal(Now, (await ctx.Municipalities.AsNoTracking().FirstAsync(m => m.Id == lgu.Id)).PayMongoLastVerifiedAtUtc);
    }

    [Fact]
    public async Task AFAILUREDoesNotEraseTheLastKnownGoodVerification()
    {
        // The fact somebody diagnosing a problem needs most: it worked, and here is when. A failed attempt today must not
        // take that away.
        var (handler, lgu, ctx) = await BuildAsync(verifierAccepts: false, alreadyVerifiedAt: Earlier);
        await using var _ctx = ctx;

        var result = await handler.Handle(new TestPaymentConnectionCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);              // the request was handled
        Assert.False(result.Value!.Ok);             // the connection was not
        Assert.Equal(Earlier, result.Value.VerifiedAtUtc);
        Assert.Equal(Earlier, (await ctx.Municipalities.AsNoTracking().FirstAsync(m => m.Id == lgu.Id)).PayMongoLastVerifiedAtUtc);
    }

    [Fact]
    public async Task TestingAnUNSAVEDKeyDoesNotClaimTheOfficeIsConnected()
    {
        // It proves the key works. It does not mean this office's connection works, and a screen whose whole job is to be
        // believed must not say otherwise.
        var (handler, lgu, ctx) = await BuildAsync(verifierAccepts: true);
        await using var _ctx = ctx;

        var result = await handler.Handle(new TestPaymentConnectionCommand("sk_live_something_else"), CancellationToken.None);

        Assert.True(result.Value!.Ok);
        Assert.Null((await ctx.Municipalities.AsNoTracking().FirstAsync(m => m.Id == lgu.Id)).PayMongoLastVerifiedAtUtc);
        Assert.Contains("Save it", result.Value.Message);
    }

    [Fact]
    public async Task TheKeyINTHEBOXIsTestedInPreferenceToTheStoredOne()
    {
        var (handler, _, ctx) = await BuildAsync(verifierAccepts: true);
        await using var _ctx = ctx;

        // Re-testing the SAME key that is stored counts as testing the office's connection, so it is stamped.
        var result = await handler.Handle(new TestPaymentConnectionCommand(StoredPlain), CancellationToken.None);

        Assert.True(result.Value!.Ok);
        Assert.Equal(Now, result.Value.VerifiedAtUtc);
    }

    [Fact]
    public async Task WithNoKeyAtAllTheOfficeIsToldToEnterOneRatherThanShownAFailure()
    {
        var (handler, _, ctx) = await BuildAsync(verifierAccepts: true, hasStoredKey: false);
        await using var _ctx = ctx;

        var result = await handler.Handle(new TestPaymentConnectionCommand(), CancellationToken.None);

        Assert.False(result.Value!.Ok);
        Assert.Contains("no secret key", result.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sk_live_abc", "Live")]
    [InlineData("sk_test_abc", "Test")]
    public async Task TheModeReportedIsTheModeOfTheKeyActuallyTested(string key, string expected)
    {
        var (handler, _, ctx) = await BuildAsync(verifierAccepts: true);
        await using var _ctx = ctx;

        var result = await handler.Handle(new TestPaymentConnectionCommand(key), CancellationToken.None);

        Assert.Equal(expected, result.Value!.Mode);
    }
}
