using EEMOCantilanSDS.Application.Command.Facilities.RemoveNpmCustomSection;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Tests for the per-LGU NPM custom-section registry: domain add/remove semantics, the repository
/// listing (registry ∪ stall names with counts), and the remove-when-empty guard.
/// </summary>
public class FacilityCustomSectionTests : RepositoryTestBase
{
    [Fact]
    public void AddCustomSection_Trims_DedupesCaseInsensitively_RejectsBlank()
    {
        var f = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        Assert.True(f.AddCustomSection("  Sari-sari Area  "));
        Assert.False(f.AddCustomSection("sari-sari area"));   // case-insensitive duplicate → no-op
        Assert.False(f.AddCustomSection("   "));              // blank → rejected
        Assert.Single(f.CustomSectionNames);
        Assert.Equal("Sari-sari Area", f.CustomSectionNames[0]);

        Assert.True(f.RemoveCustomSection("SARI-SARI AREA"));  // case-insensitive remove
        Assert.Empty(f.CustomSectionNames);
        Assert.False(f.RemoveCustomSection("Nope"));           // not present → no-op
    }

    [Fact]
    public async Task GetNpmCustomSections_ReturnsRegistryUnionStallNames_WithCounts()
    {
        var context = NewContext();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        npm.AddCustomSection("Sari-sari Area");   // registry — will have a stall
        npm.AddCustomSection("Empty Section");    // registry — no stalls (pre-declared, removable)

        var s1 = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, customSectionName: "Sari-sari Area");
        var s2 = Stall.Create(npm.Id, "2", 900m, ApplicableFees.DailyRental, customSectionName: "Legacy Area"); // stall-only, not in registry

        context.AddRange(npm, s1, s2);
        await context.SaveChangesAsync();

        var repo = new FacilityRepository(context);
        var sections = await repo.GetNpmCustomSectionsAsync(CancellationToken.None);

        Assert.Equal(3, sections.Count);
        Assert.Equal(1, sections.Single(x => x.Name == "Sari-sari Area").StallCount);
        Assert.Equal(0, sections.Single(x => x.Name == "Empty Section").StallCount);   // removable
        Assert.Equal(1, sections.Single(x => x.Name == "Legacy Area").StallCount);      // stall-derived
    }

    [Fact]
    public async Task IsStallNoUnique_ScopesByCustomSection_ForNullSectionNpmStalls()
    {
        var context = NewContext();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var s = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, customSectionName: "Sari-sari Area");
        context.AddRange(npm, s);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);

        // Same number in the SAME custom section → not unique.
        Assert.False(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, null, "Sari-sari Area", "1", CancellationToken.None));
        // Same number in a DIFFERENT custom section → unique (independent per-section numbering).
        Assert.True(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, null, "Kakanin Area", "1", CancellationToken.None));
        // Canonical sections are unaffected — "1" is free in Vegetable.
        Assert.True(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, MarketSection.VegetableArea, null, "1", CancellationToken.None));
    }

    [Fact]
    public void ResolveDailyFee_CustomSectionUsesOwnRate_CanonicalUsesOrdinance()
    {
        // Canonical stall ignores any DailyRate on it → always the caller's ordinance rate (Cantilan unchanged).
        var canonical = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental,
            section: MarketSection.VegetableArea, dailyRate: 50m);
        Assert.Equal(30m, canonical.ResolveDailyFee(30m));

        // Custom-section stall uses its own DailyRate.
        var custom = Stall.Create(Guid.NewGuid(), "2", 900m, ApplicableFees.DailyRental,
            dailyRate: 50m, customSectionName: "Sari-sari Area");
        Assert.Equal(50m, custom.ResolveDailyFee(30m));

        // Custom-section stall with no positive rate falls back to the ordinance rate.
        var customNoRate = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental,
            customSectionName: "Kakanin Area");
        Assert.Equal(30m, customNoRate.ResolveDailyFee(30m));
    }

    [Fact]
    public async Task GetStallsByFacility_PopulatesUtilityFlags_FromApplicableFees()
    {
        var context = NewContext();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        // Electricity only.
        var withElec = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental | ApplicableFees.Electricity, section: MarketSection.VegetableArea);
        // A custom section that isn't metered → no utility.
        var noUtil = Stall.Create(npm.Id, "2", 900m, ApplicableFees.DailyRental, customSectionName: "Sari-sari Area");
        context.AddRange(npm, withElec, noUtil);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var stalls = await repo.GetStallsByFacilityAsync(FacilityCode.NPM, null, CancellationToken.None);

        var e = stalls.Single(s => s.StallNo == "1");
        Assert.True(e.HasElectricity);
        Assert.False(e.HasWater);

        var n = stalls.Single(s => s.StallNo == "2");
        Assert.False(n.HasElectricity);   // → utility icon shows LOCKED for this stall
        Assert.False(n.HasWater);
    }

    [Fact]
    public async Task RemoveNpmCustomSection_IsBlocked_WhenAnyStallStillUsesIt()
    {
        var facilityRepo = new Mock<IFacilityRepository>();
        facilityRepo.Setup(r => r.GetNpmCustomSectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NpmCustomSectionDto> { new("Sari-sari Area", 2) });

        var uow = new Mock<IUnitOfWork>();
        var handler = new RemoveNpmCustomSectionCommandHandler(
            facilityRepo.Object,
            Mock.Of<ICurrentUserService>(),
            uow.Object,
            Mock.Of<IEemoCacheInvalidator>(),
            Mock.Of<ITenantContext>());

        var result = await handler.Handle(new RemoveNpmCustomSectionCommand("Sari-sari Area"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        // Guard short-circuits BEFORE loading/mutating the facility or saving.
        facilityRepo.Verify(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
