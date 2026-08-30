using EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionClosed;
using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Testing.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Facilities;

/// <summary>
/// Closing one of the office's own market sections, which closes the stalls in it as one act.
///
/// <para>
/// The office chose this knowingly, having been shown that it stops those spaces being billed, so what these tests hold is
/// that it does exactly what it says and nothing more. Two properties carry the whole design.
/// </para>
///
/// <para>
/// IT DOES NOT INVENT A FREEZE. Every stall is closed and reopened by sending <c>ToggleStallStatusCommand</c> - the path
/// the console's own per-stall control uses, which drops a stall out of the register and, on reopen, writes its frozen span
/// as excused so nothing back-bills. A second copy of that arithmetic here would be a second rule for the same money.
/// </para>
///
/// <para>
/// IT REMEMBERS WHAT IT TOUCHED. A stall the office had already closed itself is not in the closure's list and stays
/// closed when the section reopens. Reopening somebody's space months later, because a section was reopened, would be a
/// change nobody asked for and nobody would be told about.
/// </para>
/// </summary>
public class NpmSectionClosureHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    /// <summary>The office's market, with one section of its own registered.</summary>
    private static Facility Npm(string section)
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        npm.AddCustomSection(section);
        return npm;
    }

    private static Stall StallIn(Guid facilityId, string section, string no) =>
        Stall.Create(facilityId, no, 900m, ApplicableFees.DailyRental, customSectionName: section);

    /// <summary>
    /// Builds the handler over a real context, with the per-stall close recorded rather than performed: what matters here
    /// is WHICH stalls are asked to close, and the closing itself is already covered where it lives.
    /// </summary>
    private static (SetNpmSectionClosedCommandHandler handler, AppDbContext context, List<ToggleStallStatusCommand> sent)
        Build(DbContextOptions<AppDbContext> options, Facility npm, IEnumerable<Stall> stalls)
    {
        var context = new AppDbContext(options);

        var facilityRepo = new Mock<IFacilityRepository>();
        facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(npm);

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetStallsWithContractsByFacilityAsync(
                FacilityCode.NPM, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stalls.ToList());

        var sent = new List<ToggleStallStatusCommand>();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ToggleStallStatusCommand>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((c, _) => sent.Add((ToggleStallStatusCommand)c))
            .ReturnsAsync(Result<bool>.Success(true));

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("cantilan");

        var handler = new SetNpmSectionClosedCommandHandler(
            context, facilityRepo.Object, stallRepo.Object, sender.Object,
            CacheTestDoubles.Invalidator, tenant.Object, new FixedClock(new DateTime(2026, 8, 30)));

        return (handler, context, sent);
    }

    [Fact]
    public async Task ClosingASectionClosesEveryStallStillOpenInIt()
    {
        var npm = Npm("Sari-sari Area");
        var a = StallIn(npm.Id, "Sari-sari Area", "1");
        var b = StallIn(npm.Id, "Sari-sari Area", "2");

        var (handler, context, sent) = Build(Options(), npm, new[] { a, b });

        var result = await handler.Handle(new SetNpmSectionClosedCommand("Sari-sari Area", true), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.Equal(2, sent.Count);
        Assert.All(sent, c => Assert.True(c.Close));
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), sent.Select(c => c.StallId).OrderBy(x => x));

        var row = Assert.Single(await context.FacilitySectionClosures.ToListAsync());
        Assert.Equal("Sari-sari Area", row.SectionName);
        Assert.Equal(new DateOnly(2026, 8, 30), row.ClosedOn);
        Assert.Equal(2, row.ClosedStallIds.Count);
    }

    [Fact]
    public async Task AStallTheOfficeHadAlreadyClosedIsLeftOutOfTheAct()
    {
        // It is already closed, so closing it again would write a fresh ClosedAt and lose the day it actually stopped -
        // and on reopen it would be excused for a span it was not closed for.
        var npm = Npm("Sari-sari Area");
        var open = StallIn(npm.Id, "Sari-sari Area", "1");
        var alreadyClosed = StallIn(npm.Id, "Sari-sari Area", "2");
        alreadyClosed.Close(new DateOnly(2026, 3, 1));

        var (handler, context, sent) = Build(Options(), npm, new[] { open, alreadyClosed });

        var result = await handler.Handle(new SetNpmSectionClosedCommand("Sari-sari Area", true), default);

        Assert.Equal(1, result.Value);
        Assert.Equal(open.Id, Assert.Single(sent).StallId);

        var row = Assert.Single(await context.FacilitySectionClosures.ToListAsync());
        Assert.Equal(new[] { open.Id }, row.ClosedStallIds);
    }

    [Fact]
    public async Task ReopeningReturnsExactlyTheStallsTheClosureClosed()
    {
        var npm = Npm("Sari-sari Area");
        var closedByTheAct = Guid.NewGuid();
        var options = Options();

        await using (var seed = new AppDbContext(options))
        {
            seed.FacilitySectionClosures.Add(FacilitySectionClosure.Create(
                FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 20), new[] { closedByTheAct }, Tenant));
            await seed.SaveChangesAsync();
        }

        var (handler, context, sent) = Build(options, npm, Array.Empty<Stall>());

        var result = await handler.Handle(new SetNpmSectionClosedCommand("Sari-sari Area", false), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
        var asked = Assert.Single(sent);
        Assert.Equal(closedByTheAct, asked.StallId);
        Assert.False(asked.Close);                                  // reopened through the tested path, which excuses the span

        Assert.Empty(await context.FacilitySectionClosures.ToListAsync());   // the section is open again
    }

    [Fact]
    public async Task ReopeningASectionThatIsNotClosedChangesNothing()
    {
        var npm = Npm("Sari-sari Area");
        var (handler, context, sent) = Build(Options(), npm, new[] { StallIn(npm.Id, "Sari-sari Area", "1") });

        var result = await handler.Handle(new SetNpmSectionClosedCommand("Sari-sari Area", false), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Empty(sent);
        Assert.Empty(await context.FacilitySectionClosures.ToListAsync());
    }

    [Fact]
    public async Task ClosingASectionASecondTimeAddsTheStallsRecordedSince()
    {
        // An office that closes a section, records a stall in it anyway, and closes it again must not lose either stall on
        // reopen. The closure keeps both.
        var npm = Npm("Sari-sari Area");
        var first = Guid.NewGuid();
        var options = Options();

        await using (var seed = new AppDbContext(options))
        {
            seed.FacilitySectionClosures.Add(FacilitySectionClosure.Create(
                FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 20), new[] { first }, Tenant));
            await seed.SaveChangesAsync();
        }

        var later = StallIn(npm.Id, "Sari-sari Area", "9");
        var (handler, context, _) = Build(options, npm, new[] { later });

        await handler.Handle(new SetNpmSectionClosedCommand("Sari-sari Area", true), default);

        var row = Assert.Single(await context.FacilitySectionClosures.ToListAsync());
        Assert.Contains(first, row.ClosedStallIds);
        Assert.Contains(later.Id, row.ClosedStallIds);
        Assert.Equal(new DateOnly(2026, 8, 30), row.ClosedOn);
    }

    [Fact]
    public async Task ASectionTheMarketDoesNotHaveIsRefusedBeforeAnyStallIsTouched()
    {
        var npm = Npm("Sari-sari Area");
        var (handler, context, sent) = Build(Options(), npm, new[] { StallIn(npm.Id, "Sari-sari Area", "1") });

        var result = await handler.Handle(new SetNpmSectionClosedCommand("Bakery Area", true), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("not one of your market's sections", result.Error);
        Assert.Empty(sent);
        Assert.Empty(await context.FacilitySectionClosures.ToListAsync());
    }

    [Fact]
    public async Task AnUnnamedSectionIsRefused()
    {
        var npm = Npm("Sari-sari Area");
        var (handler, _, sent) = Build(Options(), npm, Array.Empty<Stall>());

        var result = await handler.Handle(new SetNpmSectionClosedCommand("   ", true), default);

        Assert.False(result.IsSuccess);
        Assert.Empty(sent);
    }
}
