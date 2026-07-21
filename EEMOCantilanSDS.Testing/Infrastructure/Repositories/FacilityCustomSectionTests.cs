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
