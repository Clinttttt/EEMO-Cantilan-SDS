using System;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// The daily fee for a stall in a market section the OFFICE named itself.
///
/// <para>
/// The three areas the platform starts with are priced by ordinance keys. A market's own section cannot be: its name is
/// whatever that LGU calls it. So until now the office priced such a section one stall at a time, typing the same figure
/// for every stall it recorded there, and a section it had not yet put anybody in could not be priced at all.
/// </para>
///
/// <para>
/// Two rulings from the office decide the rest, and these hold the rule to them: a rate applies from the day it is stated
/// and never backwards, and a stall let at its own rate keeps that rate.
/// </para>
/// </summary>
public class NpmSectionRateTests
{
    private static FeeRateSnapshot Snapshot(decimal marketRate, params FeeSectionRateEntry[] sections) =>
        new(
            new[] { new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, marketRate, new DateOnly(2026, 1, 1)) },
            sections);

    /// <summary>A stall in one of the office's own sections, optionally let at its own rate.</summary>
    private static Stall CustomSectionStall(string sectionName, decimal? ownRate)
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        return Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental,
            dailyRate: ownRate, customSectionName: sectionName);
    }

    [Fact]
    public void AnOfficesOwnSectionMayCarryItsOwnDailyFee()
    {
        var stall = CustomSectionStall("Sari-sari Area", ownRate: null);
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 29)));

        Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void AStallLetAtItsOwnRateKeepsIt_WhateverTheSectionIsPricedAt()
    {
        // The office's ruling: a stall's own rate is what it was allocated at, so a section's figure does not overrule it.
        var stall = CustomSectionStall("Sari-sari Area", ownRate: 40m);
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 29)));

        Assert.Equal(40m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void ASectionsRateIsNeverBackdated()
    {
        // The office's ruling, and the whole reason this is effective-dated: a rate stated today leaves every earlier day
        // exactly as it was billed. Settlement asks this question once per day, so an unpaid day from before the rate
        // existed still answers the market's figure.
        var stall = CustomSectionStall("Sari-sari Area", ownRate: null);
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 29)));

        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 28)));
        Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
        Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void TheLatestStatedRateAnswers()
    {
        var stall = CustomSectionStall("Sari-sari Area", ownRate: null);
        var snapshot = Snapshot(30m,
            new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)),
            new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 28m, new DateOnly(2026, 8, 20)));

        Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 19)));
        Assert.Equal(28m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 20)));
    }

    [Fact]
    public void AClearedSectionRateReturnsTheSectionToTheMarketsRate()
    {
        // Zero withdraws the figure, the same reading a cleared area rate has: an ordinance does not let a market space
        // for nothing, and clearing the row is how the office takes a section rate back.
        var stall = CustomSectionStall("Sari-sari Area", ownRate: null);
        var snapshot = Snapshot(30m,
            new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)),
            new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 0m, new DateOnly(2026, 8, 20)));

        Assert.Equal(25m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 19)));
        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 21)));
    }

    [Fact]
    public void OneSectionsRateIsNotAnothers()
    {
        var sariSari = CustomSectionStall("Sari-sari Area", ownRate: null);
        var bakery = CustomSectionStall("Bakery Area", ownRate: null);
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Sari-sari Area", 25m, new DateOnly(2026, 8, 1)));

        Assert.Equal(25m, NpmDailyFee.ForStall(sariSari, snapshot, new DateOnly(2026, 8, 29)));
        Assert.Equal(30m, NpmDailyFee.ForStall(bakery, snapshot, new DateOnly(2026, 8, 29)));

        // The office's own casing is not a second section.
        var lowered = CustomSectionStall("sari-sari area", ownRate: null);
        Assert.Equal(25m, NpmDailyFee.ForStall(lowered, snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void ACanonicalAreaIgnoresSectionRatesEntirely()
    {
        // A canonical area's price belongs to the ordinance keys, so a row naming a section cannot reach it.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var snapshot = Snapshot(30m, new FeeSectionRateEntry(FacilityCode.NPM, "Vegetable Area", 25m, new DateOnly(2026, 8, 1)));

        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
    }

    [Fact]
    public void AnOfficeThatStatesNoSectionRateIsUnchanged()
    {
        // Every office today. The old snapshot constructor still exists and carries no section rows at all, so a market's
        // own section keeps resolving through the market's rate exactly as it did.
        var stall = CustomSectionStall("Sari-sari Area", ownRate: null);
        var snapshot = new FeeRateSnapshot(
            new[] { new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1)) });

        Assert.Equal(30m, NpmDailyFee.ForStall(stall, snapshot, new DateOnly(2026, 8, 29)));
    }
}
