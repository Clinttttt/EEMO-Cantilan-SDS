using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.Testing.Mobile;

/// <summary>
/// The months a collector chooses on a space account. The rules are deliberately small and explicit: the collector picks the months, a year heading
/// picks a whole year, one month is the ordinary single item and several are one item each. Nothing here spreads a payment across months.
/// </summary>
public sealed class MonthSelectionTests
{
    private const string Op = "VEGETABLE_FRUIT_SPACE_RENTAL|a78a562cec8945b4af0cdb085f7321fb|";
    private static string Key(int year, int month) => $"{Op}{year:D4}-{month:D2}-01";
    private static string[] Year(int year) => Enumerable.Range(1, 12).Select(m => Key(year, m)).ToArray();

    [Fact]
    public void AMonthIsSelectedAndClearedByTappingItAgain_AndIsNeverInTheSelectionTwice()
    {
        var selection = new MonthSelection();

        Assert.True(selection.Toggle(Key(2023, 3)));
        Assert.Equal(1, selection.Count);
        Assert.False(selection.Toggle(Key(2023, 3)));                         // tapped again: cleared
        Assert.Equal(0, selection.Count);

        selection.Toggle(Key(2023, 3)); selection.Toggle(Key(2023, 4)); selection.Toggle(Key(2023, 4)); selection.Toggle(Key(2023, 4));
        Assert.Equal([Key(2023, 3), Key(2023, 4)], selection.Ordered());       // a key is a set member once, however often it is tapped
    }

    [Fact]
    public void OneMonthIsTheSingleItemFlow_SeveralAreMulti_AndNoneIsNeither()
    {
        var selection = new MonthSelection();
        Assert.Null(selection.Single); Assert.False(selection.IsMulti);

        selection.Toggle(Key(2023, 3));
        Assert.Equal(Key(2023, 3), selection.Single); Assert.False(selection.IsMulti);   // a part payment can be typed on one month

        selection.Toggle(Key(2023, 4));
        Assert.Null(selection.Single); Assert.True(selection.IsMulti);                    // several: one item each at its own balance

        selection.Toggle(Key(2023, 4));
        Assert.Equal(Key(2023, 3), selection.Single); Assert.False(selection.IsMulti);    // back to one
    }

    [Fact]
    public void AYearHeadingSelectsAllOfThatYear_AClearsItWhenAllAreChosen_AndCompletesAPartlyChosenYear()
    {
        var selection = new MonthSelection();
        selection.ToggleMany(Year(2023));
        Assert.Equal(12, selection.Count);

        selection.ToggleMany(Year(2023));                                     // every month already chosen: the heading clears the year
        Assert.Equal(0, selection.Count);

        selection.Toggle(Key(2023, 5));
        selection.ToggleMany(Year(2023));                                     // partly chosen: the heading completes it, it does not flip it
        Assert.Equal(12, selection.Count);
        Assert.All(Year(2023), key => Assert.True(selection.Contains(key)));
    }

    [Fact]
    public void AYearHeadingNeverTouchesAnotherYearsMonths()
    {
        var selection = new MonthSelection();
        selection.Toggle(Key(2022, 11));
        selection.ToggleMany(Year(2023));
        selection.ToggleMany(Year(2023));

        Assert.Equal([Key(2022, 11)], selection.Ordered());
    }

    [Fact]
    public void AnEmptyYearDoesNothing()
    {
        var selection = new MonthSelection();
        selection.ToggleMany([]);
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void MonthsAreOrderedOldestFirst_AcrossYears_WhateverOrderTheyWereChosenIn()
    {
        var selection = new MonthSelection();
        foreach (var key in new[] { Key(2024, 2), Key(2022, 12), Key(2023, 1), Key(2024, 1), Key(2022, 10) }) selection.Toggle(key);

        Assert.Equal([Key(2022, 10), Key(2022, 12), Key(2023, 1), Key(2024, 1), Key(2024, 2)], selection.Ordered());
    }

    [Fact]
    public void TheTotalIsTheSumOfTheChosenMonthsOwnBalances_WithAMonthTheServerNoLongerOffersCountingAsNothing()
    {
        var balances = new Dictionary<string, decimal> { [Key(2023, 3)] = 10_000m, [Key(2023, 4)] = 9_600m, [Key(2023, 5)] = 10_000m };
        var selection = new MonthSelection();
        selection.Toggle(Key(2023, 3)); selection.Toggle(Key(2023, 4));

        Assert.Equal(19_600m, selection.Total(key => balances.TryGetValue(key, out var amount) ? amount : null));

        selection.Toggle(Key(2023, 9));                                       // not offered any more (settled meanwhile): contributes nothing
        Assert.Equal(19_600m, selection.Total(key => balances.TryGetValue(key, out var amount) ? amount : null));
    }

    [Fact]
    public void OpeningAnItemToEditSelectsOnlyItsMonth_AndResetEmptiesTheSelection()
    {
        var selection = new MonthSelection();
        selection.ToggleMany(Year(2023));

        selection.Reset(Key(2024, 7));                                        // editing one existing item
        Assert.Equal(Key(2024, 7), selection.Single);

        selection.Reset();
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void ClearEmptiesEverything_SoAClosedSheetNeverCarriesAStaleSelection()
    {
        var selection = new MonthSelection();
        selection.ToggleMany(Year(2023));
        selection.Clear();

        Assert.Equal(0, selection.Count);
        Assert.False(selection.IsMulti);
        Assert.Null(selection.Single);
    }
}
