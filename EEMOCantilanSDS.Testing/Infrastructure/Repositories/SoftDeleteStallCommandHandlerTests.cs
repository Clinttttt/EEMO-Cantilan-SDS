using EEMOCantilanSDS.Application.Command.Stalls.SoftDeleteStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Guarded soft-delete of inactive stall accounts. An expired/closed account can be removed (frees the
/// stall number for reuse); a currently-active stall never can. Used to clear a bad test import.
/// </summary>
public class SoftDeleteStallCommandHandlerTests : RepositoryTestBase
{
    private static SoftDeleteStallCommandHandler Build(AppDbContext context)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("head");

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) => { await context.SaveChangesAsync(ct); });

        return new SoftDeleteStallCommandHandler(
            new StallRepository(context), currentUser.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
    }

    [Fact]
    public async Task Removes_ExpiredStall_AndFreesTheNumber()
    {
        var context = NewContext();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Contract 2020 + 3yr → already expired.
        var contract = Contract.Create(stall.Id, "Merlita A. Abuso", "Merlita A. Abuso", new DateOnly(2020, 1, 1), 3, 900m);
        context.AddRange(npm, stall, contract);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        // Before: the number is taken.
        Assert.False(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, MarketSection.FishSection, "1", CancellationToken.None));

        var result = await Build(context).Handle(new SoftDeleteStallCommand(stall.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // After: soft-deleted → the number is free again for a fresh add.
        Assert.True(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, MarketSection.FishSection, "1", CancellationToken.None));
    }

    [Fact]
    public async Task Removes_ClosedStall()
    {
        var context = NewContext();
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2_400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Frozen Lessee", "Frozen Lessee", new DateOnly(2025, 1, 1), 5, 2_400m);
        stall.Close(new DateOnly(2026, 1, 1));   // frozen (still within contract term, but Closed)
        context.AddRange(tcc, stall, contract);
        await context.SaveChangesAsync();

        var result = await Build(context).Handle(new SoftDeleteStallCommand(stall.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var repo = new StallRepository(context);
        Assert.True(await repo.IsStallNoUniqueAsync(FacilityCode.TCC, null, "7", CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_ActiveStall_WithCurrentContract()
    {
        var context = NewContext();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "5", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Contract 2025 + 5yr → still covers today.
        var contract = Contract.Create(stall.Id, "Active Vendor", "Active Vendor", new DateOnly(2025, 1, 1), 5, 900m);
        context.AddRange(npm, stall, contract);
        await context.SaveChangesAsync();

        var result = await Build(context).Handle(new SoftDeleteStallCommand(stall.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("active", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Untouched → its number is still taken.
        var repo = new StallRepository(context);
        Assert.False(await repo.IsStallNoUniqueAsync(FacilityCode.NPM, MarketSection.FishSection, "5", CancellationToken.None));
    }
}
