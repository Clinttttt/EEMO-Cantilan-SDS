using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A closed stall in a section the office closed must not offer to reopen on its own.
/// </summary>
/// <remarks>
/// Closing a section closes its stalls, and reopening the section returns exactly the stalls that closure closed. Reopening
/// ONE of them from the vendor list would put a stall back into billing inside a section the market page does not show, so its
/// arrears would accrue where nobody looks. The server refuses it as well; this states the figure the screen needs to disable
/// the control, because a control that cannot act must look as though it cannot.
/// </remarks>
public class VendorRegistrySectionClosureTests : RepositoryTestBase
{
    [Fact]
    public async Task AStallInAClosedSectionIsFlaggedSoTheListCannotOfferToReopenIt()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        // In the closed section.
        var inClosed = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental, customSectionName: "Sari Sari");
        inClosed.Close(today.AddDays(-1));
        var inClosedContract = Contract.Create(inClosed.Id, "Karmilita Log", "Karmilita Log", today.AddYears(-1), 3, 900m);

        // Closed too, but its own section is open: the office may reopen this one from the list.
        var elsewhere = Stall.Create(facility.Id, "2", 900m, ApplicableFees.BaseRental, customSectionName: "Kahoy Sale");
        elsewhere.Close(today.AddDays(-1));
        var elsewhereContract = Contract.Create(elsewhere.Id, "Pucci Lor", "Pucci Lor", today.AddYears(-1), 3, 900m);

        context.AddRange(facility, inClosed, inClosedContract, elsewhere, elsewhereContract);
        context.Add(FacilitySectionClosure.Create(FacilityCode.NPM, "Sari Sari", today.AddDays(-1), [inClosed.Id]));
        await context.SaveChangesAsync();

        var registry = await new VendorRepository(context)
            .GetVendorRegistryAsync(today.Year, today.Month, CancellationToken.None);

        Assert.True(registry.Vendors.Single(v => v.StallNo == "1").SectionClosed);
        Assert.False(registry.Vendors.Single(v => v.StallNo == "2").SectionClosed);
    }

    /// <summary>
    /// The office's own spelling of its section decides the match, not this platform's.
    /// </summary>
    /// <remarks>
    /// A closure is recorded under the name the office typed. Comparing it case-sensitively would let one stall in a closed
    /// section keep a live reopen button because somebody capitalised differently on the day it was let.
    /// </remarks>
    [Fact]
    public async Task TheSectionNameIsMatchedWithoutRegardToCaseOrSurroundingSpace()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        var stall = Stall.Create(facility.Id, "7", 900m, ApplicableFees.BaseRental, customSectionName: "  Sari Sari ");
        stall.Close(today.AddDays(-1));
        var contract = Contract.Create(stall.Id, "Karmilita Log", "Karmilita Log", today.AddYears(-1), 3, 900m);

        context.AddRange(facility, stall, contract);
        context.Add(FacilitySectionClosure.Create(FacilityCode.NPM, "SARI SARI", today.AddDays(-1), [stall.Id]));
        await context.SaveChangesAsync();

        var registry = await new VendorRepository(context)
            .GetVendorRegistryAsync(today.Year, today.Month, CancellationToken.None);

        Assert.True(registry.Vendors.Single(v => v.StallNo == "7").SectionClosed);
    }
}
