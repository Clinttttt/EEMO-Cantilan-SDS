using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Collectors;

/// <summary>
/// A collector may work only a non-facility operation (WCF, Landing/Berthing, ...). The account and its operation
/// permissions are one commit, no placeholder facility is ever assigned, and a collector must keep some legitimate work.
/// </summary>
public class OperationOnlyCollectorAccountTests
{
    private static CreateCollectorCommand Create(List<FacilityCode> facilities, List<string>? operations) => new(
        FullName: "Lorna D. Pates",
        EmployeeId: "EEMO-021",
        ContactNumber: "09171234567",
        Email: "",
        Username: "lpates",
        Password: "Str0ng-Passw0rd!",
        AssignedFacilities: facilities,
        OperationCodes: operations);

    private static Mock<ICollectorRepository> UniqueRepository()
    {
        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(r => r.IsEmployeeIdUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        collectors.Setup(r => r.IsUsernameUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        collectors.Setup(r => r.IsEmailUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return collectors;
    }

    private static Mock<ICurrentUserService> Head()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.Username).Returns("head-maria");
        return user;
    }

    [Fact]
    public async Task CreateAcceptsAnOperationOnlyCollectorButNotACollectorWithNoWork()
    {
        var validator = new CreateCollectorCommandValidator(UniqueRepository().Object);

        Assert.True((await validator.ValidateAsync(Create([], [CollectorOperationCodes.LandingBerthing]))).IsValid);
        Assert.True((await validator.ValidateAsync(Create([FacilityCode.NPM], null))).IsValid); // older clients unchanged
        Assert.False((await validator.ValidateAsync(Create([], null))).IsValid);
        Assert.False((await validator.ValidateAsync(Create([], []))).IsValid);
    }

    [Theory]
    [InlineData("UNAPPROVED_OPERATION")]
    [InlineData("ECF")]
    [InlineData("wcf")]
    [InlineData("")]
    public async Task CreateRejectsCodesOutsideTheOperationCatalog(string code)
    {
        var validator = new CreateCollectorCommandValidator(UniqueRepository().Object);

        var result = await validator.ValidateAsync(Create([], [code]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CreateRejectsADuplicatedOperation()
    {
        var validator = new CreateCollectorCommandValidator(UniqueRepository().Object);

        var result = await validator.ValidateAsync(Create([], [CollectorOperationCodes.Wcf, CollectorOperationCodes.Wcf]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CreateStagesOperationPermissionsForTheNewAccountInTheSameCommit()
    {
        var collectors = UniqueRepository();
        var uow = new Mock<IUnitOfWork>();
        Guid addedId = Guid.Empty, permittedId = Guid.Empty;
        IReadOnlyCollection<string>? permitted = null;
        string? assignedBy = null;
        var saved = false;
        collectors.Setup(r => r.AddAsync(It.IsAny<CollectorUser>(), It.IsAny<CancellationToken>()))
            .Callback<CollectorUser, CancellationToken>((c, _) => addedId = c.Id).Returns(Task.CompletedTask);
        collectors.Setup(r => r.AddOperationAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<string>, string, CancellationToken>((id, codes, by, _) =>
            {
                Assert.False(saved, "operation permissions were staged after the commit");
                permittedId = id;
                permitted = codes;
                assignedBy = by;
            })
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => saved = true).Returns(Task.CompletedTask);
        var handler = new CreateCollectorCommandHandler(collectors.Object, uow.Object, CacheTestDoubles.Invalidator,
            CacheTestDoubles.Tenant, new IdentityPasswordHasher(), Head().Object);

        var result = await handler.Handle(Create([], [CollectorOperationCodes.Wcf, CollectorOperationCodes.LandingBerthing]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(addedId, permittedId);
        Assert.Equal([CollectorOperationCodes.Wcf, CollectorOperationCodes.LandingBerthing], permitted);
        Assert.Equal("head-maria", assignedBy);
        Assert.Empty(result.Value!.AssignedFacilities); // no placeholder facility
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWithoutOperationsDoesNotTouchOperationPermissions()
    {
        var collectors = UniqueRepository();
        var handler = new CreateCollectorCommandHandler(collectors.Object, new Mock<IUnitOfWork>().Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new IdentityPasswordHasher(), Head().Object);

        await handler.Handle(Create([FacilityCode.NPM], null), CancellationToken.None);

        collectors.Verify(r => r.AddOperationAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (UpdateCollectorCommandHandler Handler, Mock<ICollectorRepository> Repo, Mock<IUnitOfWork> Uow, CollectorUser Collector)
        UpdateHandler(bool hasOperations)
    {
        var collector = CollectorUser.Create("Old Name", "EMP-1", "juan", null, null, TestPasswords.Hash("Secret123!"));
        var repo = new Mock<ICollectorRepository>();
        repo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        repo.Setup(r => r.HasOperationAssignmentsAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hasOperations);
        var uow = new Mock<IUnitOfWork>();
        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(r => r.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<FacilityCode, string>)new Dictionary<FacilityCode, string>());
        var handler = new UpdateCollectorCommandHandler(repo.Object, Head().Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, facilities.Object, new Mock<IPushSender>().Object);
        return (handler, repo, uow, collector);
    }

    [Fact]
    public async Task UpdateRefusesToLeaveACollectorWithNoFacilityAndNoOperation()
    {
        var (handler, repo, uow, collector) = UpdateHandler(hasOperations: false);

        var removeAll = await handler.Handle(new UpdateCollectorCommand(collector.Id, "N", "", "", []), CancellationToken.None);
        var clearOperations = await handler.Handle(new UpdateCollectorCommand(collector.Id, "N", "", "", [], OperationCodes: []), CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, removeAll.Status);
        Assert.Equal(ResultStatus.Invalid, clearOperations.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.ReplaceFacilityAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<List<FacilityCode>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateMayDropEveryFacilityWhenAnOperationRemains()
    {
        var (handler, repo, uow, collector) = UpdateHandler(hasOperations: true);

        var result = await handler.Handle(new UpdateCollectorCommand(collector.Id, "N", "", "", []), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        repo.Verify(r => r.ReplaceOperationAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never); // omitted list leaves permissions untouched
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateReplacesOperationPermissionsInTheSameCommitWhenSupplied()
    {
        var (handler, repo, uow, collector) = UpdateHandler(hasOperations: false);
        var saved = false;
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => saved = true).Returns(Task.CompletedTask);
        repo.Setup(r => r.ReplaceOperationAssignmentsAsync(collector.Id, It.IsAny<IReadOnlyCollection<string>>(), "head-maria", It.IsAny<CancellationToken>()))
            .Callback(() => Assert.False(saved, "operation permissions were replaced after the commit"))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new UpdateCollectorCommand(collector.Id, "N", "", "", [],
            OperationCodes: [CollectorOperationCodes.TransferLargeCattle]), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        repo.Verify(r => r.ReplaceOperationAssignmentsAsync(collector.Id,
            It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(new[] { CollectorOperationCodes.TransferLargeCattle })),
            "head-maria", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UpdateValidatorAllowsAnEmptyFacilityListButStillChecksOperationCodes()
    {
        var validator = new UpdateCollectorCommandValidator();
        var id = Guid.NewGuid();

        Assert.True(validator.Validate(new UpdateCollectorCommand(id, "N", "", "", [], OperationCodes: [CollectorOperationCodes.MarketFees])).IsValid);
        Assert.True(validator.Validate(new UpdateCollectorCommand(id, "N", "", "", [], OperationCodes: [CollectorOperationCodes.WeightAndMeasure, CollectorOperationCodes.FishMeatVendorFee, CollectorOperationCodes.Terminal])).IsValid);
        Assert.False(validator.Validate(new UpdateCollectorCommand(id, "N", "", "", [], OperationCodes: ["UNAPPROVED_OPERATION"])).IsValid);
    }
}
