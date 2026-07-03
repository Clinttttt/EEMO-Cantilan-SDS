using EEMOCantilanSDS.Application.Queries.Municipalities.GetMunicipalities;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Phase1;

/// <summary>
/// PHASE 1.5 — public municipality registry read path (backing GET /api/municipalities).
/// Verifies the query handler projects the seeded registry to DTOs through the real
/// repository/AppDbContext: default LGU first, correct status/active flags, no extra rows.
/// </summary>
public class GetMunicipalitiesQueryTests : RepositoryTestBase
{
    [Fact]
    public async Task Handler_ReturnsRegistry_DefaultFirst_WithStatusFlags()
    {
        var context = NewContext();
        await MunicipalitySeeder.SeedAsync(context);

        var handler = new GetMunicipalitiesQueryHandler(new MunicipalityRepository(context));

        var result = await handler.Handle(new GetMunicipalitiesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var list = result.Value!;
        Assert.Equal(5, list.Count);

        // Default LGU (Cantilan) ordered first, Active + IsActive + IsDefault.
        Assert.Equal("CANTILAN", list[0].Code);
        Assert.True(list[0].IsDefault);
        Assert.True(list[0].IsActive);
        Assert.Equal("Active", list[0].Status);
        Assert.Contains("EEMO", list[0].OfficeName);

        // Exactly one default.
        Assert.Single(list, m => m.IsDefault);

        // Every other LGU is an Upcoming, inactive, non-default rollout slot.
        Assert.All(list.Where(m => m.Code != "CANTILAN"), m =>
        {
            Assert.Equal("Upcoming", m.Status);
            Assert.False(m.IsActive);
            Assert.False(m.IsDefault);
            Assert.Equal("Surigao del Sur", m.Province);
        });
    }
}
