using System.Globalization;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// How the fish days of one online payment, and the kilos declared for each, are written down and read back.
///
/// <para>
/// This text is the only record of what the payor declared, and it is read while settling money already captured. So it
/// is written in one order (so the same selection is always the same text, which is what the resume guard compares), with
/// an invariant decimal point (so a machine's regional settings cannot turn 12.5 kg into 125 kg), and read back without
/// ever throwing (so a payment cannot be stranded by one unreadable entry).
/// </para>
/// </summary>
public class NpmFishDayDeclarationsTests
{
    [Fact]
    public void TheyAreWrittenInDayOrder()
    {
        var text = NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(28, 3m),
            new NpmFishDayDeclarations.Declaration(26, 12.5m),
            new NpmFishDayDeclarations.Declaration(27, 0m),
        });

        Assert.Equal("26:12.5,27:0,28:3", text);
    }

    [Fact]
    public void TheSameSelectionIsAlwaysTheSameText()
    {
        // The resume guard compares this text: two spellings of one selection would retire a good checkout every time.
        var one = NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(27, 0m),
            new NpmFishDayDeclarations.Declaration(26, 1m),
        });
        var other = NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(26, 1m),
            new NpmFishDayDeclarations.Declaration(27, 0m),
        });

        Assert.Equal(one, other);
    }

    [Fact]
    public void KilosAreWrittenWithAnInvariantDecimalPoint()
    {
        // On a machine whose regional settings use a comma for decimals, a locale-formatted 12.5 would be written "12,5"
        // — and the comma is the separator between days. The entry would then be read as two, and 12.5 kg as 125.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var text = NpmFishDayDeclarations.Format(new[] { new NpmFishDayDeclarations.Declaration(26, 12.5m) });

            Assert.Equal("26:12.5", text);
            var read = Assert.Single(NpmFishDayDeclarations.Parse(text));
            Assert.Equal(12.5m, read.Kilos);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void WhatWasWrittenIsWhatIsRead()
    {
        var declarations = new[]
        {
            new NpmFishDayDeclarations.Declaration(1, 0m),
            new NpmFishDayDeclarations.Declaration(15, 7.25m),
            new NpmFishDayDeclarations.Declaration(31, 100m),
        };

        var read = NpmFishDayDeclarations.Parse(NpmFishDayDeclarations.Format(declarations));

        Assert.Equal(declarations, read);
    }

    [Fact]
    public void ADayCannotBeCountedTwice()
    {
        var text = NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(26, 5m),
            new NpmFishDayDeclarations.Declaration(26, 9m),
        });

        Assert.Equal("26:5", text);
    }

    [Fact]
    public void ADayNoMonthHasIsRefused()
    {
        Assert.Throws<ArgumentException>(() => NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(32, 1m),
        }));
    }

    [Fact]
    public void ANegativeWeightIsRefused()
    {
        Assert.Throws<ArgumentException>(() => NpmFishDayDeclarations.Format(new[]
        {
            new NpmFishDayDeclarations.Declaration(26, -1m),
        }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("26")]
    [InlineData("26:")]
    [InlineData(":5")]
    [InlineData("0:5")]
    [InlineData("40:5")]
    [InlineData("26:-5")]
    public void UnreadableTextIsReadAsNoDays_RatherThanThrowing(string? stored)
    {
        // Read while settling money already taken: throwing here would strand the payment.
        Assert.Empty(NpmFishDayDeclarations.Parse(stored));
    }

    [Fact]
    public void OneUnreadableEntryDoesNotLoseTheOthers()
    {
        var read = NpmFishDayDeclarations.Parse("26:12.5,rubbish,27:0");

        Assert.Equal(2, read.Count);
        Assert.Equal(new NpmFishDayDeclarations.Declaration(26, 12.5m), read[0]);
        Assert.Equal(new NpmFishDayDeclarations.Declaration(27, 0m), read[1]);
    }
}
