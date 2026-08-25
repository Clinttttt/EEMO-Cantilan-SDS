using EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Collectors;

/// <summary>
/// Recording a remittance: the refusals the office asked for, and the property that matters most about a money record
/// added late to a working system, which is that nothing else moves because of it.
/// </summary>
public class RecordCollectorRemittanceTests : RepositoryTestBase
{
    private static readonly DateOnly Aug20 = new(2026, 8, 20);
    private static readonly DateOnly Aug24 = new(2026, 8, 24);
    private static readonly DateOnly Aug25 = new(2026, 8, 25);
    private static readonly DateTime NowUtc = new(2026, 8, 25, 2, 0, 0, DateTimeKind.Utc); // 10:00 AM in Cantilan

    [Fact]
    public async Task RefusesMoreThanWasCollected()
    {
        // The office was explicit: a remittance that exceeds the collection is bad design, so this is a refusal and the
        // message names both figures rather than saying the entry is invalid.
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        var result = await Validate(ctx, collector, new RecordCollectorRemittanceCommand(
            collector, 500m, Aug20, Aug25));

        Assert.False(result.IsValid);
        var message = Assert.Single(result.Errors).ErrorMessage;
        Assert.Contains("500.00", message, StringComparison.Ordinal);
        Assert.Contains("390.00", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptsExactlyWhatWasCollected()
    {
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        var result = await Validate(ctx, collector, new RecordCollectorRemittanceCommand(
            collector, 390m, Aug20, Aug25, ReferenceNo: "RCD-2026-08-021"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task RefusesDaysAnotherRemittanceAlreadyCovers()
    {
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        ctx.Add(CollectorRemittance.Create(
            collector, 200m, NowUtc, Aug20, Aug24, Guid.NewGuid(), "head", "RCD-2026-08-014", null, "head"));
        await ctx.SaveChangesAsync();

        var result = await Validate(ctx, collector, new RecordCollectorRemittanceCommand(
            collector, 100m, Aug24, Aug25));

        Assert.False(result.IsValid);
        var message = Assert.Single(result.Errors).ErrorMessage;
        Assert.Contains("already covered", message, StringComparison.Ordinal);
        Assert.Contains("RCD-2026-08-014", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesDaysThatHaveNotHappenedYet()
    {
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        var result = await Validate(ctx, collector, new RecordCollectorRemittanceCommand(
            collector, 30m, Aug25, Aug25.AddDays(3)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("not happened yet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACollectorMayNotRecordTheirOwnRemittance()
    {
        // The record exists because somebody else received the money. A collector filing their own would empty it of
        // meaning, so the handler refuses even if a route were ever opened to them.
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        var handler = Handler(ctx, asCollector: collector);
        var result = await handler.Handle(
            new RecordCollectorRemittanceCommand(collector, 100m, Aug20, Aug25), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Empty(ctx.CollectorRemittances);
    }

    [Fact]
    public async Task FilingARemittanceChangesNoCollectionAndAnswersWithThePosition()
    {
        // A remittance records custody, not what a payor owes. Every collection is left exactly as it was, and the office
        // is told the position it has just created rather than only that the save worked.
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);

        var before = ctx.DailyCollections
            .Select(d => new { d.Id, d.DailyFee, d.IsPaid, d.ORNumber, d.CollectionDate })
            .OrderBy(d => d.Id)
            .ToList();

        var result = await Handler(ctx).Handle(
            new RecordCollectorRemittanceCommand(collector, 300m, Aug20, Aug25, ReferenceNo: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(390m, result.Value!.FeeCollectionsInPeriod);
        Assert.Equal(300m, result.Value.RemittedInPeriod);
        Assert.Equal(90m, result.Value.NotYetRemittedInPeriod);
        Assert.True(result.Value.ReferenceNoMissing);   // optional, and the office is told plainly

        var after = ctx.DailyCollections
            .Select(d => new { d.Id, d.DailyFee, d.IsPaid, d.ORNumber, d.CollectionDate })
            .OrderBy(d => d.Id)
            .ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task AStatedTimeIsFiledAsTheInstantItNames()
    {
        // The office may correct the time to when the money actually changed hands. 4:40 PM in Cantilan is not 4:40 PM in
        // UTC, and filing it as though it were would put the remittance on the wrong day for anyone reading it back.
        await using var ctx = NewContext();
        var collector = await GivenACollectorWhoCollected(ctx, 390m);
        var stated = new DateTime(2026, 8, 24, 16, 40, 0);

        await Handler(ctx).Handle(
            new RecordCollectorRemittanceCommand(collector, 100m, Aug20, Aug24, ReceivedAt: stated),
            CancellationToken.None);

        var filed = Assert.Single(ctx.CollectorRemittances);
        Assert.Equal(stated, PhilippineTime.ToPhilippineTime(filed.ReceivedAt));
    }

    // ── fixtures ──

    private static async Task<Guid> GivenACollectorWhoCollected(AppDbContext ctx, decimal amount)
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var collectorId = Guid.NewGuid();

        var day = DailyCollection.Create(stall.Id, Aug24, dailyFee: amount);
        day.MarkPaid("OR-390", collectorId);
        typeof(DailyCollection).GetProperty(nameof(DailyCollection.UpdatedAt))!
            .SetValue(day, PhilippineTime.DayUtcRange(Aug24).StartUtc.AddHours(11));

        ctx.AddRange(npm, stall, day);
        await ctx.SaveChangesAsync();
        return collectorId;
    }

    private static async Task<FluentValidation.Results.ValidationResult> Validate(
        AppDbContext ctx, Guid collectorId, RecordCollectorRemittanceCommand command)
    {
        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(c => c.GetByIdAsync(collectorId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(CollectorUser.Create(
                      "Juan Dels", "EEMO-2026-001", "juan.dels", null, null, TestPasswords.Hash("Secret123!")));

        var validator = new RecordCollectorRemittanceCommandValidator(
            collectors.Object, new CollectorRemittanceRepository(ctx), new FixedClock(NowUtc));

        return await validator.ValidateAsync(command);
    }

    private static RecordCollectorRemittanceCommandHandler Handler(AppDbContext ctx, Guid? asCollector = null)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        user.SetupGet(u => u.Username).Returns("head");
        user.SetupGet(u => u.CollectorId).Returns(asCollector);

        return new RecordCollectorRemittanceCommandHandler(
            new CollectorRemittanceRepository(ctx),
            user.Object,
            new UnitOfWork(ctx),
            new FixedClock(NowUtc));
    }
}
