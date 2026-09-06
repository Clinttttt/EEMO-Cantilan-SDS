namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// Which receipt number each utility is settled under.
/// </summary>
/// <remarks>
/// <para>
/// A payor handed ONE receipt for the whole bill is the ordinary case. The collect sheet used to demand the number twice - once
/// under electricity and once under water - so the collector typed the same digits again to satisfy a field. It is now entered
/// once, and offices that do issue a receipt per utility can still say so.
/// </para>
/// <para>
/// The rule that matters is the second one: A UTILITY NOT BEING COLLECTED CARRIES NO RECEIPT NUMBER. Copying a shared number
/// onto an unpaid row would put a receipt against money nobody took, on a government record, and it would look exactly like a
/// collection when the office read it back.
/// </para>
/// <para>
/// Extracted from the razor page so it can be tested: the mobile UI has no automated coverage, and this decides what is written
/// to the office's record rather than merely how it looks.
/// </para>
/// </remarks>
public static class UtilityReceiptEntry
{
    /// <summary>
    /// The receipt number to record against each utility.
    /// </summary>
    /// <param name="separateReceipts">True where the office issued one receipt per utility.</param>
    /// <param name="sharedOr">The single receipt number, used when <paramref name="separateReceipts"/> is false.</param>
    /// <param name="elecOr">The number typed under electricity, used only when receipts are separate.</param>
    /// <param name="waterOr">The number typed under water, used only when receipts are separate.</param>
    /// <param name="collectingElec">Whether electricity is being settled at all.</param>
    /// <param name="collectingWater">Whether water is being settled at all.</param>
    public static (string ElecOr, string WaterOr) Resolve(
        bool separateReceipts,
        string? sharedOr,
        string? elecOr,
        string? waterOr,
        bool collectingElec,
        bool collectingWater)
    {
        if (separateReceipts)
        {
            return (
                collectingElec ? Trimmed(elecOr) : string.Empty,
                collectingWater ? Trimmed(waterOr) : string.Empty);
        }

        var shared = Trimmed(sharedOr);

        return (
            collectingElec ? shared : string.Empty,
            collectingWater ? shared : string.Empty);
    }

    private static string Trimmed(string? value) => (value ?? string.Empty).Trim();
}
