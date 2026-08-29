using EEMOCantilanSDS.Client.Services;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// What meters a stall's form starts with when a section is chosen.
///
/// <para>
/// The portal learned this once already, from use: clearing the fee list when a section changed stripped a stall's
/// electricity off the record the moment a clerk corrected its section. The meters belong to the SPACE, not to the
/// section it trades in. A section's metering default is therefore a suggestion for a stall being recorded and nothing
/// more, and these hold it to that — the one direction that can lose an office's data.
/// </para>
/// </summary>
public class SectionMeterDefaultsTests
{
    [Fact]
    public void ANewStallStartsWithWhatItsSectionUsuallyHas()
    {
        var fees = SectionMeterDefaults.Apply(isEditing: false, current: new[] { "DailyRental" },
            electricity: true, water: true);

        Assert.Contains("Electricity", fees);
        Assert.Contains("Water", fees);
        Assert.Contains("DailyRental", fees);   // and nothing it already carried is lost
    }

    [Fact]
    public void OnlyTheMetersTheSectionActuallyHas()
    {
        var fees = SectionMeterDefaults.Apply(isEditing: false, current: Array.Empty<string>(),
            electricity: false, water: true);

        Assert.DoesNotContain("Electricity", fees);
        Assert.Contains("Water", fees);
    }

    [Fact]
    public void AnExistingStallIsLeftExactlyAsItsRecordStands()
    {
        // The dangerous direction. A clerk correcting an existing stall's section must not have its meters rewritten: the
        // stall may sit in a wired row with no connection of its own, and that is the office's record, not an oversight.
        var fees = SectionMeterDefaults.Apply(isEditing: true, current: new[] { "DailyRental" },
            electricity: true, water: true);

        Assert.Equal(new[] { "DailyRental" }, fees);
    }

    [Fact]
    public void AnExistingStallsOwnMetersAreNeverRemovedEither()
    {
        // Nor the other way about: a stall carrying electricity in a section that is not metered keeps it.
        var fees = SectionMeterDefaults.Apply(isEditing: true, current: new[] { "Electricity" },
            electricity: false, water: false);

        Assert.Contains("Electricity", fees);
    }

    [Fact]
    public void ASectionWithNoMetersAddsNothing()
    {
        var fees = SectionMeterDefaults.Apply(isEditing: false, current: new[] { "DailyRental" },
            electricity: false, water: false);

        Assert.Equal(new[] { "DailyRental" }, fees);
    }

    [Fact]
    public void AMeterAlreadyTickedIsNotDoubled()
    {
        var fees = SectionMeterDefaults.Apply(isEditing: false, current: new[] { "Electricity" },
            electricity: true, water: false);

        Assert.Single(fees.Where(f => f == "Electricity"));
    }
}
