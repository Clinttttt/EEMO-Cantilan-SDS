using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which Stall / Space No. each row of an imported batch ends up with.
///
/// <para>
/// The screen used to rewrite the WHOLE batch from the facility's highest number as soon as any single cell was blank,
/// duplicated, or already taken. The office's own lists are routinely mixed — numbered stalls beside un-numbered
/// spaces — so one blank cell renumbered every other row in the file. On a second or partial import into a populated
/// facility that silently overwrote the physical stall numbers its collections are keyed on, and nothing on screen
/// said so.
/// </para>
/// </summary>
public class ImportNumberingTests
{
    private static (string?, bool) Stall(string? no) => (no, true);
    private static (string?, bool) Space(string? no = null) => (no, false);

    [Fact]
    public void OneBlankCell_DoesNotRenumberTheRestOfTheFile()
    {
        // The defect, stated plainly. Rows 1 and 3 carry the office's own numbers; row 2 arrived blank.
        var result = ImportNumbering.Assign(
            new[] { Stall("101"), Stall(null), Stall("103") },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal("101", result[0].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[0].Outcome);

        Assert.Equal("103", result[2].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[2].Outcome);

        // Only the blank one was given anything, and it did not take a number the file already used.
        Assert.Equal(NumberingOutcome.NumberedBlank, result[1].Outcome);
        Assert.NotEqual("101", result[1].StallNo);
        Assert.NotEqual("103", result[1].StallNo);
    }

    [Fact]
    public void ASecondImportIntoAPopulatedFacility_LeavesTheExistingNumbersAlone()
    {
        // The scenario that loses data: the office adds three more stalls to a facility that already holds 1-5, and
        // one of the new rows happens to clash. Only the clashing row may move.
        var result = ImportNumbering.Assign(
            new[] { Stall("6"), Stall("3"), Stall("7") },
            activeNumbers: new[] { "1", "2", "3", "4", "5" },
            highestStallNo: 5,
            highestSpaceOrdinal: 0);

        Assert.Equal("6", result[0].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[0].Outcome);

        Assert.Equal(NumberingOutcome.RenumberedClash, result[1].Outcome);
        Assert.NotEqual("3", result[1].StallNo);

        Assert.Equal("7", result[2].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[2].Outcome);
    }

    [Fact]
    public void TheOfficesMixedList_KeepsItsStallNumbersAndNumbersOnlyTheSpaces()
    {
        // Exactly the shape of a Tampak Commercial Center sheet: three contracted stalls, then two spaces held
        // without a contract, which the office does not number and leaves blank.
        var result = ImportNumbering.Assign(
            new[] { Stall("1"), Stall("2"), Stall("3"), Space(), Space() },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal(new[] { "1", "2", "3" }, result.Take(3).Select(r => r.StallNo));
        Assert.All(result.Take(3), r => Assert.Equal(NumberingOutcome.Kept, r.Outcome));

        Assert.Equal("SP-1", result[3].StallNo);
        Assert.Equal("SP-2", result[4].StallNo);
        Assert.All(result.Skip(3), r => Assert.Equal(NumberingOutcome.NumberedSpace, r.Outcome));
    }

    [Fact]
    public void ASpaceNeverTakesAStallNumber_EvenIfTheCellHasOne()
    {
        // The office does not number these at all, so a number typed into that cell is not one of its stall numbers.
        // Keeping it would spend a number belonging to a real stall.
        var result = ImportNumbering.Assign(
            new[] { Space("4"), Stall("4") },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal("SP-1", result[0].StallNo);
        Assert.Equal(NumberingOutcome.NumberedSpace, result[0].Outcome);

        // And stall 4 remains available to the row that genuinely is stall 4.
        Assert.Equal("4", result[1].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[1].Outcome);
    }

    [Fact]
    public void TwoRowsClaimingTheSameNumber_LeaveTheFirstAloneAndMoveTheSecond()
    {
        var result = ImportNumbering.Assign(
            new[] { Stall("9"), Stall("9") },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal("9", result[0].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[0].Outcome);
        Assert.Equal(NumberingOutcome.RenumberedClash, result[1].Outcome);
        Assert.NotEqual("9", result[1].StallNo);
    }

    [Fact]
    public void AVacatedNumberIsNotAClash_BecauseReclaimingItIsARenewal()
    {
        // Closed and lapsed stalls are excluded from the active set by the caller, so a row reclaiming one is the
        // office renewing that stall. Renumbering it would create a second stall beside the one being renewed.
        var result = ImportNumbering.Assign(
            new[] { Stall("2") },
            activeNumbers: new[] { "1", "3" },   // 2 is vacated, so it is absent
            highestStallNo: 3,
            highestSpaceOrdinal: 0);

        Assert.Equal("2", result[0].StallNo);
        Assert.Equal(NumberingOutcome.Kept, result[0].Outcome);
    }

    [Fact]
    public void AssignedNumbersSkipEverythingAlreadyHeld_IncludingWhatThisBatchJustTook()
    {
        var result = ImportNumbering.Assign(
            new[] { Stall(null), Stall(null), Stall(null) },
            activeNumbers: new[] { "1", "2", "4" },
            highestStallNo: 4,
            highestSpaceOrdinal: 0);

        var assigned = result.Select(r => r.StallNo).ToList();
        Assert.Equal(assigned.Count, assigned.Distinct().Count());
        Assert.DoesNotContain("1", assigned);
        Assert.DoesNotContain("2", assigned);
        Assert.DoesNotContain("4", assigned);
    }

    [Fact]
    public void TheSpaceSeriesContinuesFromWhatTheFacilityAlreadyHas()
    {
        var result = ImportNumbering.Assign(
            new[] { Space(), Space() },
            activeNumbers: new[] { "1", "SP-1", "SP-2" },
            highestStallNo: 1,
            highestSpaceOrdinal: 2);

        Assert.Equal("SP-3", result[0].StallNo);
        Assert.Equal("SP-4", result[1].StallNo);
    }

    [Fact]
    public void AClashingRowNeverStealsANumberAnotherRowSupplied()
    {
        // The assigned number must step over numbers appearing LATER in the file too, or resolving one clash would
        // create another the office never had.
        var result = ImportNumbering.Assign(
            new[] { Stall("1"), Stall("1"), Stall("2") },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal("1", result[0].StallNo);
        Assert.Equal("2", result[2].StallNo);
        // Row 2 clashed and must not be handed "2", which row 3 supplied.
        Assert.NotEqual("2", result[1].StallNo);
        Assert.Equal(3, result.Select(r => r.StallNo).Distinct().Count());
    }

    [Fact]
    public void ACleanFileIsLeftEntirelyUntouched()
    {
        var result = ImportNumbering.Assign(
            new[] { Stall("101"), Stall("102"), Stall("103") },
            activeNumbers: new[] { "1", "2" },
            highestStallNo: 2,
            highestSpaceOrdinal: 0);

        Assert.All(result, r => Assert.Equal(NumberingOutcome.Kept, r.Outcome));
        Assert.Equal(new[] { "101", "102", "103" }, result.Select(r => r.StallNo));
    }

    [Fact]
    public void RemovingARow_ClosesUpOnlyTheNumbersThisScreenHandedOut()
    {
        // Three the office supplied and three this screen gave out. Removing one of ours must close the gap in OURS
        // and leave the office's exactly as written - the whole batch being renumbered is what once overwrote the
        // physical stall numbers a facility's collections are keyed on.
        var supplied = new[] { "101", "102", "103" };

        // What the screen hands out continues after the facility's highest active number.
        var first = ImportNumbering.Assign(
            new[] { Stall("101"), Stall("102"), Stall("103"), Stall(null), Stall(null), Stall(null) },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal(supplied, first.Take(3).Select(r => r.StallNo));
        var assigned = first.Skip(3).Select(r => r.StallNo).ToList();
        Assert.Equal(3, assigned.Distinct().Count());

        // Now the middle assigned row is removed. Re-running with ours blanked is exactly what the screen does.
        var after = ImportNumbering.Assign(
            new[] { Stall("101"), Stall("102"), Stall("103"), Stall(null), Stall(null) },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal(supplied, after.Take(3).Select(r => r.StallNo));
        Assert.Equal(assigned.Take(2), after.Skip(3).Select(r => r.StallNo));
    }

    [Fact]
    public void ReNumberingOursNeverTakesANumberTheOfficeSupplied()
    {
        // The office's numbers are reserved before anything is handed out, so closing a gap in ours cannot land on
        // one of theirs.
        var result = ImportNumbering.Assign(
            new[] { Stall(null), Stall("2"), Stall(null), Stall("4") },
            activeNumbers: Array.Empty<string>(),
            highestStallNo: 0,
            highestSpaceOrdinal: 0);

        Assert.Equal("2", result[1].StallNo);
        Assert.Equal("4", result[3].StallNo);

        var ours = new[] { result[0].StallNo, result[2].StallNo };
        Assert.DoesNotContain("2", ours);
        Assert.DoesNotContain("4", ours);
        Assert.Equal(2, ours.Distinct().Count());
    }
}
