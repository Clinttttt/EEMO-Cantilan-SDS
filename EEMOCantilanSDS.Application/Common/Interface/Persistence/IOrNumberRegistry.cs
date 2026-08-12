namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Whether an Official Receipt number is still free to use in this LGU.
///
/// <para>
/// OR numbers are copied by hand from the LGU's receipt booklets, and one number belongs to one TRANSACTION: it must never
/// appear against another payor or in another module. That makes availability an LGU-wide question spanning monthly
/// rentals, NPM daily collections, slaughterhouse, Tabo-an, the transport terminal and NPM utility bills — so asking it
/// through whichever module repository a handler happens to hold was misleading. Five repository interfaces each offered
/// the same method; a reader could reasonably think the market's check and the terminal's check were different rules.
/// </para>
///
/// <para>
/// A number is unavailable even when the record holding it was soft-deleted, because a receipt written in a booklet cannot
/// be un-written. Availability is scoped to the current municipality, so a second LGU may legitimately use a number that
/// exists only in another.
/// </para>
/// </summary>
public interface IOrNumberRegistry
{
    /// <summary>
    /// True when <paramref name="orNumber"/> is not yet used anywhere in this LGU. A blank number is treated as available:
    /// it means no receipt has been written yet, which is a different matter from a clash.
    /// </summary>
    Task<bool> IsAvailableAsync(string orNumber, CancellationToken ct = default);

    /// <summary>
    /// The same question asked while re-marking a utility bill, which may keep the number already on it — one receipt may
    /// cover both the electricity and the water side of the same bill.
    /// </summary>
    Task<bool> IsAvailableForUtilityBillAsync(string orNumber, Guid? excludeBillId, CancellationToken ct = default);
}
