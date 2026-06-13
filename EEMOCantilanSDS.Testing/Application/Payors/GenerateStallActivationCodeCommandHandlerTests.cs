using EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payors;

public class GenerateStallActivationCodeCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code)
    {
        var stall = Stall.Create(Guid.NewGuid(), "12", 2400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        return stall;
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Maria", "EEMO-2026-009", "maria", "maria@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    private static (GenerateStallActivationCodeCommandHandler handler, Mock<IPayorRepository> payorRepo)
        Build(Stall stall, CollectorUser? collector, string? role, Guid? collectorId)
    {
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var collectorRepo = new Mock<ICollectorRepository>();
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);

        var payorRepo = new Mock<IPayorRepository>();
        payorRepo.Setup(r => r.ActivationCodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        // Defaults: number not registered and no pending code elsewhere — overridden per guard test.
        payorRepo.Setup(r => r.GetByContactNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PayorUser?)null);
        payorRepo.Setup(r => r.LinkExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        payorRepo.Setup(r => r.ActiveCodeExistsForContactOnOtherStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        var uow = new Mock<IUnitOfWork>();

        return (new GenerateStallActivationCodeCommandHandler(
            stallRepo.Object, collectorRepo.Object, payorRepo.Object, currentUser.Object, uow.Object), payorRepo);
    }

    [Fact]
    public async Task Collector_NotAssignedToFacility_IsForbidden()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var collector = CollectorWith(FacilityCode.NCC);
        var (handler, payorRepo) = Build(stall, collector, "Collector", collector.Id);

        var result = await handler.Handle(
            new GenerateStallActivationCodeCommand(stall.Id, "09171234567", null), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        payorRepo.Verify(r => r.AddActivationCodeAsync(It.IsAny<PayorActivationCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_Assigned_IssuesCode_AndRevokesPrior()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var collector = CollectorWith(FacilityCode.TCC);
        var (handler, payorRepo) = Build(stall, collector, "Collector", collector.Id);

        PayorActivationCode? captured = null;
        payorRepo.Setup(r => r.AddActivationCodeAsync(It.IsAny<PayorActivationCode>(), It.IsAny<CancellationToken>()))
            .Callback<PayorActivationCode, CancellationToken>((c, _) => captured = c).Returns(Task.CompletedTask);

        var result = await handler.Handle(
            new GenerateStallActivationCodeCommand(stall.Id, " 09171234567 ", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.Code));
        Assert.Equal("09171234567", result.Value.ContactNumber);   // trimmed
        Assert.Contains("-", result.Value.Code);                    // XXXX-XXXX format
        Assert.NotNull(captured);
        Assert.Equal("09171234567", captured!.ContactNumber);
        payorRepo.Verify(r => r.RevokeActiveCodesForStallAsync(stall.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Admin_IsNotAssignmentRestricted()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, payorRepo) = Build(stall, collector: null, "Admin", collectorId: null);

        var result = await handler.Handle(
            new GenerateStallActivationCodeCommand(stall.Id, "09171234567", 14), CancellationToken.None);

        Assert.True(result.IsSuccess);
        payorRepo.Verify(r => r.AddActivationCodeAsync(It.IsAny<PayorActivationCode>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NumberAlreadyRegisteredToAccount_IsConflict_AndIssuesNoCode()
    {
        // The bug's source: a number already owned by a payor must not get a new code, or activation
        // would merge a second occupant's stall onto that account.
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, payorRepo) = Build(stall, collector: null, "Admin", collectorId: null);
        payorRepo.Setup(r => r.GetByContactNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PayorUser.Create("Diego Villafuerte", "09171234567", "Secret123!"));

        var result = await handler.Handle(
            new GenerateStallActivationCodeCommand(stall.Id, "09171234567", null), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        payorRepo.Verify(r => r.AddActivationCodeAsync(It.IsAny<PayorActivationCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NumberHasPendingCodeForAnotherStall_IsConflict_AndIssuesNoCode()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, payorRepo) = Build(stall, collector: null, "Admin", collectorId: null);
        payorRepo.Setup(r => r.ActiveCodeExistsForContactOnOtherStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await handler.Handle(
            new GenerateStallActivationCodeCommand(stall.Id, "09171234567", null), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        payorRepo.Verify(r => r.AddActivationCodeAsync(It.IsAny<PayorActivationCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
