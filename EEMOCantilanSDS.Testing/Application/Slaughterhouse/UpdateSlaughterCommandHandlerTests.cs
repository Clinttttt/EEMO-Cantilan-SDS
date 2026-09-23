using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class UpdateSlaughterCommandHandlerTests
{
    private static readonly DateOnly ActivityDate = new(2026, 6, 15);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static AppDbContext NewContext(DbContextOptions<AppDbContext> options, Guid municipalityId) =>
        new(options, new FixedMunicipality(municipalityId));

    private static (UpdateSlaughterCommandHandler Handler, Mock<ISlaughterRepository> SlaughterRepo,
        List<SlaughterTransaction> Added, List<SlaughterTransaction> Removed) Build(IFeeRateResolver rates)
    {
        var slaughterRepo = new Mock<ISlaughterRepository>();
        var facilityRepo = new Mock<IFacilityRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var added = new List<SlaughterTransaction>();
        var removed = new List<SlaughterTransaction>();

        facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.SLH, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"));
        currentUser.SetupGet(u => u.Role).Returns("Admin");
        currentUser.SetupGet(u => u.Username).Returns("head");
        slaughterRepo.Setup(r => r.GetTransactionsByOwnerDateORAsync(
                It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SlaughterTransaction>)Array.Empty<SlaughterTransaction>());
        slaughterRepo.Setup(r => r.AddAsync(It.IsAny<SlaughterTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<SlaughterTransaction, CancellationToken>((transaction, _) => added.Add(transaction))
            .Returns(Task.CompletedTask);
        slaughterRepo.Setup(r => r.RemoveAsync(It.IsAny<SlaughterTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<SlaughterTransaction, CancellationToken>((transaction, _) => removed.Add(transaction))
            .Returns(Task.CompletedTask);

        var handler = new UpdateSlaughterCommandHandler(
            slaughterRepo.Object,
            facilityRepo.Object,
            collectorRepo.Object,
            currentUser.Object,
            unitOfWork.Object,
            CacheTestDoubles.Invalidator,
            rates,
            CacheTestDoubles.Tenant);

        return (handler, slaughterRepo, added, removed);
    }

    private static IFeeRateResolver Resolver(params FeeRateEntry[] entries)
    {
        var mock = new Mock<IFeeRateResolver>();
        mock.Setup(r => r.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeeRateSnapshot(entries));
        return mock.Object;
    }

    [Fact]
    public async Task Update_UsesConfiguredRatesEffectiveOnTheOriginalActivityDate_ForHogAndSharedLargeAnimals()
    {
        var rates = Resolver(
            new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 420m, new DateOnly(2020, 1, 1)),
            new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 510m, new DateOnly(2026, 7, 1)),
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 860m, new DateOnly(2020, 1, 1)),
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 925m, new DateOnly(2026, 7, 1)));
        var (handler, _, added, _) = Build(rates);

        var result = await handler.Handle(new UpdateSlaughterCommand(
            "Owner A", ActivityDate, "OR-OLD", [
                new(AnimalType.Hog, null, 2, null),
                new(AnimalType.Carabao, null, 1, null),
                new(AnimalType.Cow, null, 3, null)
            ]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(added.OrderBy(t => t.AnimalType),
            hog =>
            {
                Assert.Equal(420m, hog.RatePerHead);
                Assert.Equal(840m, hog.TotalAmount);
                Assert.Equal(FeeRates.SlhHogSlaughterFee, hog.SlaughterFee);
            },
            carabao =>
            {
                Assert.Equal(860m, carabao.RatePerHead);
                Assert.Equal(860m, carabao.TotalAmount);
            },
            cow =>
            {
                Assert.Equal(860m, cow.RatePerHead);
                Assert.Equal(2_580m, cow.TotalAmount);
            });
        Assert.All(added, transaction =>
        {
            Assert.Equal(ActivityDate, transaction.TransactionDate);
            Assert.Equal("OR-OLD", transaction.ORNumber);
        });
    }

    [Fact]
    public async Task Update_PreservesCustomAnimalRateWithoutRequiringCanonicalRateRows()
    {
        var (handler, _, added, _) = Build(Resolver());

        var result = await handler.Handle(new UpdateSlaughterCommand(
            "Owner A", ActivityDate, "OR-CUSTOM", [new(AnimalType.Other, "Goat", 2, 252m)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var transaction = Assert.Single(added);
        Assert.Equal(AnimalType.Other, transaction.AnimalType);
        Assert.Equal("Goat", transaction.CustomAnimalType);
        Assert.Equal(252m, transaction.RatePerHead);
        Assert.Equal(504m, transaction.TotalAmount);
    }

    [Fact]
    public async Task Update_UsesOnlyTheCurrentMunicipalityRates()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var municipalityA = Guid.NewGuid();
        var municipalityB = Guid.NewGuid();
        var effective = new DateOnly(2020, 1, 1);

        using (var seed = NewContext(options, municipalityA))
        {
            seed.FacilityRates.Add(FacilityRate.Create(
                FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 440m, effective, municipalityA));
            await seed.SaveChangesAsync();
        }

        using var contextA = NewContext(options, municipalityA);
        var (handlerA, _, rowsA, _) = Build(new FeeRateResolver(contextA));
        var resultA = await handlerA.Handle(new UpdateSlaughterCommand(
            "Owner A", ActivityDate, "OR-A", [new(AnimalType.Hog, null, 1, null)]), CancellationToken.None);

        using var contextB = NewContext(options, municipalityB);
        var (handlerB, repoB, rowsB, _) = Build(new FeeRateResolver(contextB));
        var resultB = await handlerB.Handle(new UpdateSlaughterCommand(
            "Owner B", ActivityDate, "OR-B", [new(AnimalType.Hog, null, 1, null)]), CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.Equal(440m, Assert.Single(rowsA).RatePerHead);
        Assert.False(resultB.IsSuccess);
        Assert.Empty(rowsB);
        repoB.Verify(r => r.AddAsync(It.IsAny<SlaughterTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
