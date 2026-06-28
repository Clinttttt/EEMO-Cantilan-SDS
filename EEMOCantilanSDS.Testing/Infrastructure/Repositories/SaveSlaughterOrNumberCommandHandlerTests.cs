using EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Adding an OR to a slaughter visit. A visit (owner + date) is recorded as one row per animal type
/// but is a SINGLE receipt, so one OR is stamped across every animal row of that visit. A different
/// date is an independent receipt.
/// </summary>
public class SaveSlaughterOrNumberCommandHandlerTests : RepositoryTestBase
{
    private static readonly DateOnly Day = new(2026, 6, 20);

    private static (SaveSlaughterOrNumberCommandHandler handler, Mock<IUnitOfWork> uow) BuildHandler(IReadOnlyList<SlaughterTransaction> rows)
    {
        var repo = new Mock<ISlaughterRepository>();
        repo.Setup(r => r.GetUnreceiptedByOwnerDateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("admin");
        var uow = new Mock<IUnitOfWork>();
        return (new SaveSlaughterOrNumberCommandHandler(repo.Object, user.Object, uow.Object), uow);
    }

    [Fact]
    public async Task StampsOneOr_OnEveryAnimalRowOfTheVisit()
    {
        // One visit, two animal types → two rows, but one receipt.
        var hog = SlaughterTransaction.CreateHog(Guid.NewGuid(), null, "Maria Santos", 1, "", Day);
        var cow = SlaughterTransaction.CreateLargeAnimal(Guid.NewGuid(), null, "Maria Santos", AnimalType.Cow, 1, "", Day);
        var (handler, uow) = BuildHandler(new[] { hog, cow });

        var result = await handler.Handle(
            new SaveSlaughterOrNumberCommand("Maria Santos", Day, "OR-77"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("OR-77", hog.ORNumber);
        Assert.Equal("OR-77", cow.ORNumber);   // same OR across both animal rows of the one visit
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenNoUnreceiptedRowsForVisit()
    {
        var (handler, uow) = BuildHandler(Array.Empty<SlaughterTransaction>());

        var result = await handler.Handle(
            new SaveSlaughterOrNumberCommand("Nobody", Day, "OR-1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUnreceiptedByOwnerDate_ReturnsOnlyBlankOrRowsForThatVisit()
    {
        await using var ctx = NewContext();
        var fid = Guid.NewGuid();
        var nextDay = Day.AddDays(1);

        ctx.Add(SlaughterTransaction.CreateHog(fid, null, "Maria Santos", 1, "", Day));                      // blank, this visit
        ctx.Add(SlaughterTransaction.CreateLargeAnimal(fid, null, "Maria Santos", AnimalType.Cow, 1, "", Day)); // blank, this visit
        ctx.Add(SlaughterTransaction.CreateHog(fid, null, "Maria Santos", 1, "OR-9", Day));                  // already receipted
        ctx.Add(SlaughterTransaction.CreateHog(fid, null, "Maria Santos", 1, "", nextDay));                  // a different visit
        await ctx.SaveChangesAsync();
        var repo = new SlaughterRepository(ctx);

        var rows = await repo.GetUnreceiptedByOwnerDateAsync("Maria Santos", Day, CancellationToken.None);

        Assert.Equal(2, rows.Count);                       // only the two blank-OR rows of this visit
        Assert.All(rows, r => Assert.True(string.IsNullOrEmpty(r.ORNumber)));
        Assert.All(rows, r => Assert.Equal(Day, r.TransactionDate));
    }
}
