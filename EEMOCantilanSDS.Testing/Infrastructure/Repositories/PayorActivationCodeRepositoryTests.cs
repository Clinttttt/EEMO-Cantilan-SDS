using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Activation codes must not accumulate: re-issuing for a stall replaces the prior code so the table
/// holds exactly one record per stall, and codes for other stalls are untouched.
/// </summary>
public class PayorActivationCodeRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task RemoveCodesForStall_HardDeletesAll_OneRecordRemainsAfterReissue()
    {
        var context = NewContext();
        var stallId = Guid.NewGuid();
        var otherStallId = Guid.NewGuid();

        context.AddRange(
            PayorActivationCode.Create("AAAA-BBBB", "09171111111", stallId, DateTime.UtcNow.AddDays(30)),
            PayorActivationCode.Create("CCCC-DDDD", "09172222222", stallId, DateTime.UtcNow.AddDays(30)),
            PayorActivationCode.Create("EEEE-FFFF", "09173333333", otherStallId, DateTime.UtcNow.AddDays(30)));
        await context.SaveChangesAsync();

        var repo = new PayorRepository(context, new Moq.Mock<EEMOCantilanSDS.Application.Common.Payments.INpmMonthSettlementService>().Object);

        // Re-issue: remove prior code(s) for the stall, then add the new one.
        await repo.RemoveCodesForStallAsync(stallId);
        await repo.AddActivationCodeAsync(
            PayorActivationCode.Create("GGGG-HHHH", "09174444444", stallId, DateTime.UtcNow.AddDays(30)));
        await context.SaveChangesAsync();

        var forStall = await context.PayorActivationCodes.IgnoreQueryFilters()
            .Where(c => c.StallId == stallId).ToListAsync();
        Assert.Single(forStall);                       // exactly one record per stall
        Assert.Equal("GGGG-HHHH", forStall[0].Code);   // and it is the newly issued one

        // Codes for a different stall are not affected.
        var forOther = await context.PayorActivationCodes.IgnoreQueryFilters()
            .CountAsync(c => c.StallId == otherStallId);
        Assert.Equal(1, forOther);
    }
}
