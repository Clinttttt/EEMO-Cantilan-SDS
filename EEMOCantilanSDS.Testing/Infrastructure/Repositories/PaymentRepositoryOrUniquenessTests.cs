using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class PaymentRepositoryOrUniquenessTests : RepositoryTestBase
{
    // Regression: OR numbers must be globally unique across modules. A payment OR that already
    // exists on a slaughter receipt must be rejected (previously only Payments+Daily were checked).
    [Fact]
    public async Task IsORNumberUnique_FalseWhenUsedInAnotherModule()
    {
        await using var ctx = NewContext();

        ctx.Add(SlaughterTransaction.CreateHog(
            Guid.NewGuid(), Guid.NewGuid(), "owner", heads: 1, orNumber: "OR-X", transactionDate: new DateOnly(2026, 1, 1)));
        await ctx.SaveChangesAsync();

        var repo = new PaymentRepository(ctx);

        Assert.False(await repo.IsORNumberUniqueAsync("OR-X", CancellationToken.None));
        Assert.True(await repo.IsORNumberUniqueAsync("OR-UNUSED", CancellationToken.None));
    }
}
