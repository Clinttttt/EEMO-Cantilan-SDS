using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Monitoring;

public sealed class ContractsMonitoringTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Theory]
    [InlineData(-1, ContractMonitoringStatus.Expired)]
    [InlineData(0, ContractMonitoringStatus.ExpiringSoon)]
    [InlineData(7, ContractMonitoringStatus.ExpiringSoon)]
    [InlineData(30, ContractMonitoringStatus.ExpiringSoon)]
    [InlineData(31, ContractMonitoringStatus.Active)]
    public void ExpiryIsInclusiveAndMonitoringWindowsUseDays(int days, ContractMonitoringStatus expected)
    {
        var start = Today.AddDays(days + 1).AddYears(-1);
        var raw = new ContractMonitoringRecord { OccupancyBasis = OccupancyArrangement.SignedContract, OccupancyStart = start, EffectivityDate = start, DurationYears = 1 };
        var row = ContractMonitoringRules.WithExpiry().Compile()(raw);
        Assert.Equal(DomainRules.TermLastDay(start, 1), row.ExpiryDate);
        Assert.Equal(expected, ContractMonitoringRules.Status(Today).Compile()(row));
        Assert.Equal(days is >= 0 and <= 7, ContractMonitoringRules.InWindow(Today, 7).Compile()(row));
        Assert.Equal(days is >= 0 and <= 30, ContractMonitoringRules.InWindow(Today, 30).Compile()(row));
    }

    [Theory]
    [InlineData(OccupancyArrangement.SpaceOnly)]
    [InlineData(OccupancyArrangement.Extension)]
    public void NoContractNeverExposesSentinelOrContractExpiry(OccupancyArrangement basis)
    {
        var row = ContractMonitoringRules.WithExpiry().Compile()(new() { OccupancyBasis = basis,
            OccupancyStart = Today.AddYears(-50), EffectivityDate = Today.AddYears(-50), DurationYears = DomainRules.OpenEndedTermYears });
        Assert.Null(row.ExpiryDate);
        Assert.Equal(ContractMonitoringStatus.NoContract, ContractMonitoringRules.Status(Today).Compile()(row));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void MissingInvalidOrUnrepresentableTermsStayUnknown(int? years)
    {
        var row = ContractMonitoringRules.WithExpiry().Compile()(new() { OccupancyBasis = OccupancyArrangement.SignedContract,
            OccupancyStart = Today.AddYears(-1), EffectivityDate = Today.AddYears(-1), DurationYears = years });
        Assert.Null(row.ExpiryDate); Assert.Equal(ContractMonitoringStatus.Unknown, ContractMonitoringRules.Status(Today).Compile()(row));
    }

    [Fact]
    public void EndedAndFutureEvidenceTakePrecedenceAndDoNotInferContractDates()
    {
        var project = ContractMonitoringRules.WithExpiry().Compile(); var status = ContractMonitoringRules.Status(Today).Compile();
        Assert.Equal(ContractMonitoringStatus.Unknown, status(project(new() { OccupancyStart = Today.AddYears(-3), OccupancyBasis = OccupancyArrangement.SignedContract, DurationYears = 3 })));
        Assert.Equal(ContractMonitoringStatus.Unknown, status(project(new() { OccupancyStart = Today })));
        Assert.Equal(ContractMonitoringStatus.Ended, status(project(new() { Ended = true, OccupancyStart = Today.AddDays(1), OccupancyBasis = OccupancyArrangement.SpaceOnly })));
        Assert.Equal(ContractMonitoringStatus.NotYetEffective, status(project(new() { OccupancyStart = Today.AddDays(1), OccupancyBasis = OccupancyArrangement.SpaceOnly })));
        Assert.Equal(ContractMonitoringStatus.NotYetEffective, status(project(new() { OccupancyStart = Today.AddDays(-1), OccupancyBasis = OccupancyArrangement.SignedContract, EffectivityDate = Today.AddDays(1), DurationYears = 3 })));
    }

    [Theory]
    [InlineData("Collector", true, false, false, ResultStatus.Forbidden)]
    [InlineData("Payor", true, false, false, ResultStatus.Forbidden)]
    [InlineData("Admin", false, false, false, ResultStatus.Unauthorized)]
    [InlineData("Admin", true, true, false, ResultStatus.Forbidden)]
    [InlineData("SuperAdmin", true, false, true, ResultStatus.Forbidden)]
    public async Task HandlerFailsClosedBeforeCallingPersistence(string role, bool authenticated, bool mismatch, bool unresolved, ResultStatus expected)
    {
        var tenant = Guid.NewGuid(); var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(authenticated); user.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        user.SetupGet(x => x.Role).Returns(role); user.SetupGet(x => x.MunicipalityId).Returns(mismatch ? Guid.NewGuid() : tenant);
        var municipality = new Mock<ICurrentMunicipalityAccessor>(); municipality.SetupGet(x => x.MunicipalityId).Returns(unresolved ? Guid.Empty : tenant);
        var queries = new Mock<IContractsMonitoringQueries>(MockBehavior.Strict);
        var result = await new GetContractsMonitoringQueryHandler(queries.Object, user.Object, municipality.Object, Mock.Of<IClock>()).Handle(new(new()), default);
        Assert.Equal(expected, result.Status); Assert.Empty(queries.Invocations);
    }

    [Fact]
    public async Task HandlerUsesOnePhilippineDayAndRejectsUnsupportedAsOf()
    {
        var tenant = Guid.NewGuid(); var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true); user.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        user.SetupGet(x => x.Role).Returns("Admin"); user.SetupGet(x => x.MunicipalityId).Returns(tenant);
        var municipality = new Mock<ICurrentMunicipalityAccessor>(); municipality.SetupGet(x => x.MunicipalityId).Returns(tenant);
        var clock = new Mock<IClock>(); clock.SetupGet(x => x.PhilippineToday).Returns(Today);
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 10, 13, 16, 1, 0, DateTimeKind.Utc));
        var queries = new Mock<IContractsMonitoringQueries>();
        queries.Setup(x => x.GetAsync(It.IsAny<ContractsMonitoringRequest>(), Today, It.IsAny<CancellationToken>())).ReturnsAsync(new ContractsMonitoringPageDto(Today, new(0, 0, 0, 0, 0, 0, 0, 0, 0), 0, 1, 25, []));
        var handler = new GetContractsMonitoringQueryHandler(queries.Object, user.Object, municipality.Object, clock.Object);
        Assert.True((await handler.Handle(new(new()), default)).IsSuccess);
        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new(new() { AsOf = Today.AddDays(-1) }), default)).Status);
        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new(new() { AsOf = Today.AddDays(1) }), default)).Status);
        queries.Verify(x => x.GetAsync(It.IsAny<ContractsMonitoringRequest>(), Today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ValidatorAcceptsNamedFiltersAndRejectsUnboundedQueriesAndNumericEnums()
    {
        var validator = new GetContractsMonitoringQueryValidator();
        Assert.True(validator.Validate(new GetContractsMonitoringQuery(new() { Operation = "ncc", Status = "expiringsoon", OccupancyBasis = "signedcontract", ExpiryWindow = 7 })).IsValid);
        Assert.True(validator.Validate(new GetContractsMonitoringQuery(new() { Operation = "TRM" })).IsValid); // Eligibility still comes from the stored rental archetype.
        var invalid = new ContractsMonitoringRequest { Search = new string('x', 201), Page = 0, PageSize = 101, Sort = "sql", Status = "1", OccupancyBasis = "2", Operation = "FA", FacilityId = Guid.Empty, ExpiryWindow = 15 };
        Assert.Equal(9, validator.Validate(new GetContractsMonitoringQuery(invalid)).Errors.Count);
        Assert.False(validator.Validate(new GetContractsMonitoringQuery(new() { Page = 10001 })).IsValid);
    }
}
