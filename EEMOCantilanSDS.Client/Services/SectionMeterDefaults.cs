namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// What meters a stall's form starts with when a market section is chosen.
///
/// <para>
/// Extracted from the market page so the one dangerous direction can be tested. The meters belong to the SPACE, not to
/// the section it trades in: the portal already learned that once, when clearing the list on a section change stripped a
/// stall's electricity off the record the moment a clerk corrected its section. A section's metering default must
/// therefore only ever ADD a suggestion to a stall being recorded, and must say nothing at all while an existing stall is
/// being edited.
/// </para>
/// </summary>
public static class SectionMeterDefaults
{
    /// <summary>
    /// The fee types the form should carry after a section is chosen.
    /// </summary>
    /// <param name="isEditing">True while an existing stall is open. The default is silent then: its meters are its own record.</param>
    /// <param name="current">What the form carries now. Nothing here is ever removed.</param>
    /// <param name="electricity">Whether the chosen section is usually metered for electricity.</param>
    /// <param name="water">Whether the chosen section is usually metered for water.</param>
    public static List<string> Apply(bool isEditing, IEnumerable<string>? current, bool electricity, bool water)
    {
        var fees = (current ?? Array.Empty<string>()).ToList();
        if (isEditing) return fees;

        if (electricity && !fees.Contains("Electricity")) fees.Add("Electricity");
        if (water && !fees.Contains("Water")) fees.Add("Water");

        return fees;
    }
}
