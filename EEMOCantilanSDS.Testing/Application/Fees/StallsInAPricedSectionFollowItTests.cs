using System;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// What a stall recorded in a PRICED section carries as its own rate — which is nothing.
///
/// <para>
/// Found by auditing the section-fee work rather than by a report from the office, and it would have made the whole
/// feature inert. Two paths create market stalls: the stall form and the stallholder import. Both stamped a figure onto
/// <c>Stall.DailyRate</c> for a custom-section stall — the clerk's, else another stall's in that section, else the
/// market's. A stall's OWN rate outranks its section's for ever, so an office that priced a section at ₱25 would have gone
/// on collecting ₱30 from every stall either path created, with both figures defensible on their own screen.
/// </para>
///
/// <para>
/// So where the section carries a stated fee, a stall nobody priced individually carries no rate of its own and resolves
/// through the section. Where the section carries none, both paths behave exactly as they did.
/// </para>
/// </summary>
public class StallsInAPricedSectionFollowItTests
{
    private static FeeRateSnapshot Snapshot(decimal marketRate, params FeeSectionRateEntry[] sections) =>
        new(
            new[] { new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, marketRate, new DateOnly(2026, 1, 1)) },
            sections);

    private static Stall StallIn(string section, decimal? ownRate)
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        return Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental,
            dailyRate: ownRate, customSectionName: section);
    }

    [Fact]
    public void WithNoRateOfItsOwn_ItIsBilledItsSectionsFee()
    {
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)));

        Assert.Equal(25m, NpmDailyFee.ForStall(StallIn("Sari-sari Area", ownRate: null), snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void StampedWithTheMarketsRate_TheSectionsFeeWouldNeverApply()
    {
        // The state both create paths used to leave behind. Stated as a test so the cost of that stamp is on the record:
        // ₱30 collected from a stall in a section the office priced at ₱25.
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)));

        Assert.Equal(30m, NpmDailyFee.ForStall(StallIn("Sari-sari Area", ownRate: 30m), snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void AStallThePriceWasSetForIndividuallyStillKeepsIt()
    {
        // The office's ruling: an own rate is what that space was allocated at, and it is not overruled by the section.
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)));

        Assert.Equal(40m, NpmDailyFee.ForStall(StallIn("Sari-sari Area", ownRate: 40m), snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void InAnUnpricedSection_NothingAboutTheOldBehaviourMoves()
    {
        // Every office today: no section fee stated, so a stall carrying the market's rate and one carrying none are both
        // billed the market's rate, which is what both create paths have always produced.
        var snapshot = Snapshot(30m);

        Assert.Equal(30m, NpmDailyFee.ForStall(StallIn("Sari-sari Area", ownRate: 30m), snapshot, new DateOnly(2026, 8, 29)));
        Assert.Equal(30m, NpmDailyFee.ForStall(StallIn("Sari-sari Area", ownRate: null), snapshot, new DateOnly(2026, 8, 29)));
    }
}
