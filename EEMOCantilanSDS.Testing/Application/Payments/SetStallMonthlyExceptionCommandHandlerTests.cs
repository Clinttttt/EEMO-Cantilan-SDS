using EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Monthly excused exceptions apply only to monthly-rental facilities (TCC/NCC/BBQ/ICE). NPM (daily)
/// must be rejected — it uses per-day DailyCollection.IsAbsent instead.
/// </summary>
public class SetStallMonthlyExceptionCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 2400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        return stall;
    }

    private static (SetStallMonthlyExceptionCommandHandler handler, Mock<IStallMonthlyExceptionRepository> repo) Build(Stall stall)
    {
        var repo = new Mock<IStallMonthlyExceptionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StallMonthlyException?)null);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new SetStallMonthlyExceptionCommandHandler(repo.Object, stallRepo.Object, currentUser.Object, uow.Object), repo);
    }

    [Fact]
    public async Task MonthlyFacility_CreatesException()
    {
        var (handler, repo) = Build(StallInFacility(FacilityCode.TCC));

        var result = await handler.Handle(
            new SetStallMonthlyExceptionCommand(Guid.NewGuid(), 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<StallMonthlyException>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Npm_IsRejected_NoExceptionCreated()
    {
        var (handler, repo) = Build(StallInFacility(FacilityCode.NPM));

        var result = await handler.Handle(
            new SetStallMonthlyExceptionCommand(Guid.NewGuid(), 2026, 6), CancellationToken.None);

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<StallMonthlyException>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
