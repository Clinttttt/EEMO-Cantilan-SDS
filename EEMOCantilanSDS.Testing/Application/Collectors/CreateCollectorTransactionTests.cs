using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Collectors;

/// <summary>
/// Creating a collector is one fact about the office: this person collects, for these facilities.
///
/// <para>
/// It used to be written in two commits — the account, then the facilities it was assigned to. A failure between them left
/// a collector who could sign in but was assigned nowhere, so their app opened with nothing to collect for. That reads as a
/// broken account rather than an incomplete one, and the office's remedy (create the collector again) would then fail on
/// the unique employee ID.
/// </para>
/// </summary>
public class CreateCollectorTransactionTests
{
    private static CreateCollectorCommand Command() => new(
        FullName: "Rogelio B. Uy",
        EmployeeId: "EEMO-014",
        ContactNumber: "09171234567",
        Email: "r.uy@example.gov.ph",
        Username: "ruy",
        Password: "Str0ng-Passw0rd!",
        AssignedFacilities: [FacilityCode.NPM, FacilityCode.TCC]);

    private static (CreateCollectorCommandHandler handler, Mock<ICollectorRepository> collectors, Mock<IUnitOfWork> uow) Build()
    {
        var collectors = new Mock<ICollectorRepository>();
        var uow = new Mock<IUnitOfWork>();

        collectors.Setup(r => r.IsEmployeeIdUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        collectors.Setup(r => r.IsUsernameUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        collectors.Setup(r => r.IsEmailUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateCollectorCommandHandler(
            collectors.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new IdentityPasswordHasher());

        return (handler, collectors, uow);
    }

    [Fact]
    public async Task TheAccountAndItsFacilityAssignmentsAreOneCommit()
    {
        var (handler, collectors, uow) = Build();

        await handler.Handle(Command(), CancellationToken.None);

        collectors.Verify(r => r.AddAsync(It.IsAny<CollectorUser>(), It.IsAny<CancellationToken>()), Times.Once);
        collectors.Verify(r => r.AddFacilityAssignmentsAsync(
            It.IsAny<Guid>(), It.IsAny<List<FacilityCode>>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheAssignmentsAreWrittenBeforeTheCommit_ForTheAccountJustCreated()
    {
        // Ordering matters as much as the count: the assignments must be staged against THIS collector's id and be part
        // of the same commit, not written afterwards against an account that may not have landed.
        var (handler, collectors, uow) = Build();

        Guid addedCollectorId = Guid.Empty;
        Guid assignedCollectorId = Guid.Empty;
        var saved = false;

        collectors.Setup(r => r.AddAsync(It.IsAny<CollectorUser>(), It.IsAny<CancellationToken>()))
            .Callback<CollectorUser, CancellationToken>((c, _) => addedCollectorId = c.Id)
            .Returns(Task.CompletedTask);
        collectors.Setup(r => r.AddFacilityAssignmentsAsync(
                It.IsAny<Guid>(), It.IsAny<List<FacilityCode>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, List<FacilityCode>, CancellationToken>((id, _, _) =>
            {
                assignedCollectorId = id;
                Assert.False(saved, "the assignments were staged after the commit, so they are not part of it");
            })
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saved = true)
            .Returns(Task.CompletedTask);

        await handler.Handle(Command(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, addedCollectorId);
        Assert.Equal(addedCollectorId, assignedCollectorId);
        Assert.True(saved);
    }
}
