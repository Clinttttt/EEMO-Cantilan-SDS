using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionUtilities;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Facilities;

/// <summary>
/// Whether stalls in one of the office's own market sections are usually metered.
///
/// <para>
/// A DEFAULT for a stall being recorded there, and these hold it to that. The meters belong to the space: the portal
/// already refuses to strip a stall's electricity or water when a clerk corrects its section, and this must not become a
/// second way to do the same thing. So it changes no stall, bills nothing, and is only ever read by the form.
/// </para>
/// </summary>
public class NpmSectionUtilitiesHandlerTests
{
    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static (SetNpmSectionUtilitiesCommandHandler handler, AppDbContext context) Build(
        DbContextOptions<AppDbContext> options, params string[] registeredSections)
    {
        var context = new AppDbContext(options);

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        foreach (var s in registeredSections) npm.AddCustomSection(s);

        var facilityRepo = new Mock<IFacilityRepository>();
        facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(npm);

        return (new SetNpmSectionUtilitiesCommandHandler(
            context, facilityRepo.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), context);
    }

    [Fact]
    public async Task TheSectionsMeteringIsRecordedOnce_AndChangedInPlace()
    {
        var options = Options();

        var (handler, context) = Build(options, "Sari-sari Area");
        await using (context)
        {
            Assert.True((await handler.Handle(new SetNpmSectionUtilitiesCommand("Sari-sari Area", true, false), default)).IsSuccess);
            Assert.True((await handler.Handle(new SetNpmSectionUtilitiesCommand("Sari-sari Area", true, true), default)).IsSuccess);
        }

        await using var verify = new AppDbContext(options);
        // One row, not a history: a default bills nothing and there is nothing to reconcile against later.
        var row = Assert.Single(await verify.FacilitySectionUtilities.ToListAsync());
        Assert.True(row.Electricity);
        Assert.True(row.Water);
    }

    [Fact]
    public async Task ItIsRecordedUnderTheNameTheOfficeRegistered()
    {
        // Whatever casing the caller sends, so the form's lookup and this row cannot describe two different sections.
        var options = Options();

        var (handler, context) = Build(options, "Sari-sari Area");
        await using (context)
        {
            Assert.True((await handler.Handle(new SetNpmSectionUtilitiesCommand("  sari-sari area  ", true, true), default)).IsSuccess);
        }

        await using var verify = new AppDbContext(options);
        Assert.Equal("Sari-sari Area", (await verify.FacilitySectionUtilities.SingleAsync()).SectionName);
    }

    [Fact]
    public async Task ASectionTheOfficeHasNotRegisteredIsRefused()
    {
        // Otherwise a typo leaves a row describing a section that does not exist, in the office's own records, for ever.
        var options = Options();

        var (handler, context) = Build(options, "Sari-sari Area");
        await using (context)
        {
            var result = await handler.Handle(new SetNpmSectionUtilitiesCommand("Bakery Area", true, true), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultStatus.Invalid, result.Status);
        }

        await using var verify = new AppDbContext(options);
        Assert.Empty(await verify.FacilitySectionUtilities.ToListAsync());
    }

    [Fact]
    public async Task NoStallIsTouched()
    {
        // The whole point: this is a default for a stall not yet recorded. A stall already in the section keeps exactly the
        // meters its own record carries — including none.
        var options = Options();

        var (handler, context) = Build(options, "Sari-sari Area");
        await using (context)
        {
            var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
            var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, customSectionName: "Sari-sari Area");
            context.Facilities.Add(npm);
            context.Stalls.Add(stall);
            await context.SaveChangesAsync();

            Assert.True((await handler.Handle(new SetNpmSectionUtilitiesCommand("Sari-sari Area", true, true), default)).IsSuccess);
        }

        await using var verify = new AppDbContext(options);
        var untouched = await verify.Stalls.SingleAsync();
        Assert.Equal(ApplicableFees.DailyRental, untouched.Fees);
        Assert.False(untouched.Fees.HasFlag(ApplicableFees.Electricity));
        Assert.False(untouched.Fees.HasFlag(ApplicableFees.Water));
    }

    [Fact]
    public async Task AnEmptyNameIsRefused()
    {
        var options = Options();
        var (handler, context) = Build(options, "Sari-sari Area");

        await using (context)
        {
            var result = await handler.Handle(new SetNpmSectionUtilitiesCommand("   ", true, true), default);
            Assert.Equal(ResultStatus.Invalid, result.Status);
        }
    }
}
