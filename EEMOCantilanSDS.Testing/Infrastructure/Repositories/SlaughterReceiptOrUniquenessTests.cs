using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

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

    // The owner is only a typed name — there is no client record behind it — so "a different owner" has to mean a different
    // PERSON and not a different spelling. These are the cases that refused the office its own receipt: the first animal was
    // entered one way, the second another, and the OR already written on the paper came back as unavailable.

    [Theory]
    [InlineData("alan cayetano")]            // all lower
    [InlineData("ALAN CAYETANO")]            // all caps
    [InlineData("Alan  Cayetano")]           // double space between names
    [InlineData("  Alan Cayetano  ")]        // padded, as pasted
    [InlineData("Alan cayetano")]            // inconsistent capitalisation
    public async Task SameOwnerTypedDifferently_StillAllowsTheReceiptsOr(string asTypedAgain)
    {
        await using var ctx = NewContext();
        ctx.Add(Hog("Alan Cayetano", "OR-1", Day));
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.True(
            await repo.IsORNumberAvailableForReceiptAsync("OR-1", asTypedAgain, Day, CancellationToken.None),
            $"'{asTypedAgain}' is the same person as the name on the receipt, so the receipt's OR must still be usable");
    }

    [Fact]
    public async Task StoredNameKeepsTheSpellingTheClerkTyped()
    {
        // Matching ignores case; storage must not. The office's documents print the name as it was entered, so only
        // redundant whitespace is removed.
        await using var ctx = NewContext();
        ctx.Add(Hog("  Alan   Cayetano ", "OR-3", Day));
        await ctx.SaveChangesAsync();

        var stored = await ctx.SlaughterTransactions.SingleAsync(x => x.ORNumber == "OR-3");
        Assert.Equal("Alan Cayetano", stored.OwnerName);
    }

    [Fact]
    public async Task ADifferentPersonIsStillRejected_EvenWithSimilarSpacing()
    {
        // The relaxation must not become "any name will do": a genuinely different person may not take the OR.
        await using var ctx = NewContext();
        ctx.Add(Hog("Alan Cayetano", "OR-1", Day));
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        Assert.False(await repo.IsORNumberAvailableForReceiptAsync("OR-1", "  ALAN  CAYETANA ", Day, CancellationToken.None));
        Assert.False(await repo.IsORNumberAvailableForReceiptAsync("OR-1", "Alan Cayetano Jr", Day, CancellationToken.None));
    }
}
