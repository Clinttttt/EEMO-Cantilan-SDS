using EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Adding a transporter is idempotent on plate number: the mobile "Record a Trip" quick flow
/// re-submits the same plate on every trip, and must reuse the existing transporter rather than
/// creating a duplicate (which would pollute the roster and inflate the transporter count).
/// </summary>
public class AddTransporterCommandHandlerTests
{
    private static AddTransporterCommand Command(string plate) =>
        new("Driver A", "Org", "Route 1", plate, null);

    [Fact]
    public async Task ExistingPlate_ReusesTransporter_DoesNotCreateDuplicate()
    {
        var existing = TrmTransporter.Create("Driver A", "Org", "Route 1", "ABC 123");
        var repo = new Mock<ITrmRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetTransporterByPlateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new AddTransporterCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(Command("abc 123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value!.Id);
        repo.Verify(r => r.AddTransporterAsync(It.IsAny<TrmTransporter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewPlate_CreatesTransporter()
    {
        var repo = new Mock<ITrmRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetTransporterByPlateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrmTransporter?)null);

        var handler = new AddTransporterCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(Command("XYZ 789"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddTransporterAsync(It.IsAny<TrmTransporter>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
