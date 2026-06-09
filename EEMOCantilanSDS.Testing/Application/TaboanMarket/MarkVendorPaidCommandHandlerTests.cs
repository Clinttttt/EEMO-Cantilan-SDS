using EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Marking a Tabo-an vendor paid is shared by web admins and mobile collectors. Collectors may
/// only collect if assigned to TPM; admins are unrestricted. A provided OR number is attached.
/// </summary>
public class MarkVendorPaidCommandHandlerTests
{
    private static (MarkVendorPaidCommandHandler handler, TpmAttendance attendance) Build(
        CollectorUser? collector, string? role, Guid? collectorId)
    {
        var attendance = TpmAttendance.Create(Guid.NewGuid(), new DateOnly(2026, 6, 5)); // a Friday
        var tpmRepo = new Mock<ITpmRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        tpmRepo.Setup(r => r.GetAttendanceByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(attendance);
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new MarkVendorPaidCommandHandler(tpmRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object), attendance);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Nena", "EEMO-2026-008", "nena", "nena@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task Collector_NotAssignedToTpm_IsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, attendance) = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(new MarkVendorPaidCommand(attendance.Id, true, "OR-1"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        Assert.False(attendance.IsPaid);
    }

    [Fact]
    public async Task Collector_AssignedToTpm_MarksPaid_WithOrNumber()
    {
        var collector = CollectorWith(FacilityCode.TPM);
        var (handler, attendance) = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(new MarkVendorPaidCommand(attendance.Id, true, "OR-123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(attendance.IsPaid);
        Assert.Equal("OR-123", attendance.ORNumber);
        Assert.Equal(collector.Id, attendance.CollectorId);
    }
}
