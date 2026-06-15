using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// SLH OR rule: the same OR may repeat across the animal-type lines of ONE receipt
/// (same owner + same date), but not for a different owner/date or any other module.
/// </summary>
public class SlaughterReceiptOrUniquenessTests : RepositoryTestBase
{
    private static readonly DateOnly Day = new(2026, 6, 9);

    private static SlaughterTransaction Hog(string owner, string orNumber, DateOnly date) =>
        SlaughterTransaction.CreateHog(Guid.NewGuid(), Guid.NewGuid(), owner, heads: 1, orNumber: orNumber, transactionDate: date);

    [Fact]
    public async Task SameOwnerAndDate_AllowsSameOr_ForAnotherAnimalLine()
    {
        await using var ctx = NewContext();
        ctx.Add(Hog("Alan Cayetano", "OR-1", Day));
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.True(await repo.IsORNumberAvailableForReceiptAsync("OR-1", "Alan Cayetano", Day, CancellationToken.None));
    }

    [Fact]
    public async Task DifferentOwner_SameOr_IsRejected()
    {
        await using var ctx = NewContext();
        ctx.Add(Hog("Alan Cayetano", "OR-1", Day));
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.False(await repo.IsORNumberAvailableForReceiptAsync("OR-1", "Donya Laras", Day, CancellationToken.None));
    }

    [Fact]
    public async Task DifferentDate_SameOr_IsRejected()
    {
        await using var ctx = NewContext();
        ctx.Add(Hog("Alan Cayetano", "OR-1", Day));
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.False(await repo.IsORNumberAvailableForReceiptAsync("OR-1", "Alan Cayetano", Day.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task OrUsedInAnotherModule_IsRejected()
    {
        await using var ctx = NewContext();
        var payment = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, 2400m, "tester");
        payment.UpdateStatus(PaymentStatus.Paid, 0m, null, "tester", null);
        payment.SetOrNumber("OR-2", "tester");
        ctx.Add(payment);
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.False(await repo.IsORNumberAvailableForReceiptAsync("OR-2", "Alan Cayetano", Day, CancellationToken.None));
    }

    [Fact]
    public async Task BrandNewOr_IsAllowed()
    {
        await using var ctx = NewContext();
        var repo = new SlaughterRepository(ctx);

        Assert.True(await repo.IsORNumberAvailableForReceiptAsync("OR-NEW", "Alan Cayetano", Day, CancellationToken.None));
    }
}
