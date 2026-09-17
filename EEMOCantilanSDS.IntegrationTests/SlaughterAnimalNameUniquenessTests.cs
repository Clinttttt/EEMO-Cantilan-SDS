using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>PostgreSQL, rather than only the command validator, owns custom-animal name uniqueness per LGU.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SlaughterAnimalNameUniquenessTests(PostgresFixture db)
{
    [SkippableTheory]
    [InlineData("Goat", "goat")]
    [InlineData("Goat", "GOAT")]
    public async Task OneMunicipalityCannotPersistCaseVariants(string first, string second)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var municipalityId = Guid.NewGuid();

        await using (var write = db.CreateContext(municipalityId))
        {
            write.SlaughterAnimalRates.Add(SlaughterAnimalRate.Create(first, 252m, municipalityId));
            await write.SaveChangesAsync();
        }

        await using var duplicate = db.CreateContext(municipalityId);
        duplicate.SlaughterAnimalRates.Add(SlaughterAnimalRate.Create(second, 252m, municipalityId));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Contains("UX_SlaughterAnimalRates_Municipality_NormalizedName", error.InnerException?.Message ?? error.Message);
    }

    [SkippableFact]
    public async Task DifferentMunicipalitiesMayUseTheSameNormalizedName()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var firstMunicipality = Guid.NewGuid();
        var secondMunicipality = Guid.NewGuid();

        await using (var first = db.CreateContext(firstMunicipality))
        {
            first.SlaughterAnimalRates.Add(SlaughterAnimalRate.Create("Goat", 252m, firstMunicipality));
            await first.SaveChangesAsync();
        }

        await using (var second = db.CreateContext(secondMunicipality))
        {
            second.SlaughterAnimalRates.Add(SlaughterAnimalRate.Create("GOAT", 300m, secondMunicipality));
            await second.SaveChangesAsync();
        }

        await using var firstRead = db.CreateContext(firstMunicipality);
        await using var secondRead = db.CreateContext(secondMunicipality);
        Assert.Equal("Goat", (await firstRead.SlaughterAnimalRates.SingleAsync()).AnimalName);
        Assert.Equal("GOAT", (await secondRead.SlaughterAnimalRates.SingleAsync()).AnimalName);
    }
}
