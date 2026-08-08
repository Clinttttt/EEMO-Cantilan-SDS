namespace EEMOCantilanSDS.Domain.Common;

/// <summary>What happened to one row's Stall / Space No. when a batch was imported.</summary>
public enum NumberingOutcome
{
    /// <summary>The office supplied a usable number and it was left exactly as written.</summary>
    Kept = 0,

    /// <summary>The number was already held by an active stall, or claimed by an earlier row in the same file.</summary>
    RenumberedClash = 1,

    /// <summary>The row arrived with no number and was given the next free one.</summary>
    NumberedBlank = 2,

    /// <summary>A space held without a signed contract, which the office does not number at all.</summary>
    NumberedSpace = 3,
}

/// <summary>The number a row ends up with, and why.</summary>
/// <param name="StallNo">The value to record.</param>
/// <param name="Outcome">Why it is that value, so the screen can tell the office what changed.</param>
public sealed record NumberingDecision(string StallNo, NumberingOutcome Outcome);

/// <summary>
/// Decides the Stall / Space No. for each row of an imported batch.
///
/// <para>
/// This rule used to live inside the import screen and rewrote the WHOLE batch from the facility's highest number the
/// moment any single cell was blank, duplicated, or already taken. The office's own lists are routinely mixed —
/// numbered stalls beside un-numbered spaces — so one blank cell renumbered every other row in the file. On a second
/// or partial import into a populated facility that silently overwrote the physical stall numbers its collections are
/// keyed on, and the office had no way to see it had happened.
/// </para>
///
/// <para>
/// Each row is now decided on its own, and the rule lives here so it can be tested: a number the office wrote is the
/// office's to keep unless something already holds it.
/// </para>
/// </summary>
public static class ImportNumbering
{
    /// <param name="rows">Each row's supplied number and whether it is held under a signed contract, in file order.</param>
    /// <param name="activeNumbers">
    /// Numbers held by stalls that are still active. Vacated numbers — closed or lapsed — must NOT be included: a row
    /// reclaiming one is the office renewing that stall, not a clash.
    /// </param>
    /// <param name="highestStallNo">The facility's highest active stall number, so new numbers continue after it.</param>
    /// <param name="highestSpaceOrdinal">The highest space ordinal already used, so that series continues too.</param>
    public static IReadOnlyList<NumberingDecision> Assign(
        IEnumerable<(string? SuppliedStallNo, bool HasSignedContract)> rows,
        IEnumerable<string> activeNumbers,
        int highestStallNo,
        int highestSpaceOrdinal)
    {
        var ordered = rows.ToList();
        var taken = new HashSet<string>(
            activeNumbers.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        // ── Pass one: reserve every number the office supplied, before anything is handed out. ──
        // Without this, resolving one row's clash could take a number a LATER row had supplied, turning one clash into
        // two and moving a stall the office had numbered correctly. First occurrence wins; a repeat is a clash.
        var keep = new bool[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            var (supplied, signed) = ordered[i];
            if (!signed) continue;

            var number = (supplied ?? string.Empty).Trim();
            if (number.Length == 0) continue;

            // A space identifier against a signed contract is not the office's numbering either — it can only have got
            // there by editing, and keeping it would file a contracted stall under the un-numbered series.
            if (SpaceNumber.IsSpace(number)) continue;

            if (taken.Add(number)) keep[i] = true;
        }

        // ── Pass two: give a number to the rows that still need one. ──
        var nextStall = highestStallNo + 1;
        var nextSpace = highestSpaceOrdinal + 1;
        var decisions = new List<NumberingDecision>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var (supplied, signed) = ordered[i];

            if (!signed)
            {
                // The office issues no number for these, so whatever sits in the cell is not one.
                decisions.Add(new NumberingDecision(SpaceNumber.Format(nextSpace++), NumberingOutcome.NumberedSpace));
                continue;
            }

            if (keep[i])
            {
                decisions.Add(new NumberingDecision((supplied ?? string.Empty).Trim(), NumberingOutcome.Kept));
                continue;
            }

            var wasBlank = string.IsNullOrWhiteSpace(supplied);
            decisions.Add(new NumberingDecision(
                NextFree(ref nextStall, taken),
                wasBlank ? NumberingOutcome.NumberedBlank : NumberingOutcome.RenumberedClash));
        }

        return decisions;
    }

    /// <summary>The next stall number nothing holds, counting the ones just handed out in this batch.</summary>
    private static string NextFree(ref int next, HashSet<string> taken)
    {
        while (taken.Contains(next.ToString())) next++;
        var assigned = next.ToString();
        taken.Add(assigned);
        next++;
        return assigned;
    }
}
