using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Infrastructure.Persistence;

public class AppDbContextSoftDeleteFilterTests
{
    [Fact]
    public async Task AuditableEntities_ExcludeSoftDeletedRowsByDefault()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var active = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var deleted = Stall.Create(Guid.NewGuid(), "A-2", 900m, ApplicableFees.DailyRental);
        deleted.SoftDelete("head");

        await using var context = new AppDbContext(options);
        context.Stalls.AddRange(active, deleted);
        await context.SaveChangesAsync();

        var visibleStalls = await context.Stalls.ToListAsync();
        var allStalls = await context.Stalls.IgnoreQueryFilters().ToListAsync();

        Assert.Single(visibleStalls);
        Assert.Equal(active.Id, visibleStalls[0].Id);
        Assert.Equal(2, allStalls.Count);
    }
}
