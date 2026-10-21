using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

public sealed class CollectionReconciliationQueryTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Collector", true)]
    [InlineData("SuperAdmin", false)]
    public async Task Monthly_income_adjustments_reject_non_Head_or_foreign_callers_before_any_persistence(string role, bool sameTenant)
    {
        var tenantId = Guid.NewGuid(); var db = new Mock<IAppDbContext>(MockBehavior.Strict);
        var user = Mock.Of<ICurrentUserService>(x => x.IsAuthenticated && x.UserId == Guid.NewGuid() && x.Role == role
            && x.MunicipalityId == (sameTenant ? tenantId : Guid.NewGuid()));
        var flow = new EEMOCantilanSDS.Application.Common.Revenue.ReportGovernanceWorkflow(db.Object, user,
            Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenantId), new FixedClock(DateTime.UtcNow), Mock.Of<ILegacyMonthlyIncomeReader>());
        Assert.Equal(ResultStatus.Forbidden, (await flow.AdjustAsync(new(Guid.NewGuid(), "RENT_NPM", 2026, 10, 900, 850, "Office reason"))).Status);
        db.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Collector", true, true, ResultStatus.Forbidden)]
    [InlineData("SuperAdmin", false, true, ResultStatus.Forbidden)]
    [InlineData("Admin", true, false, ResultStatus.Unauthorized)]
    public async Task Role_and_tenant_boundaries_precede_reads(string role, bool sameTenant, bool authenticated, ResultStatus status)
    {
        var tenantId = Guid.NewGuid(); var reader = new Mock<ICollectionActivityReader>();
        var user = Mock.Of<ICurrentUserService>(x => x.Role == role && x.IsAuthenticated == authenticated && x.UserId == Guid.NewGuid()
            && x.MunicipalityId == (sameTenant ? tenantId : Guid.NewGuid()));
        var flow = new GetCollectionReconciliationQueryHandler(reader.Object, user,
            Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenantId), new FixedClock(new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(status, (await flow.Handle(new(new(2026, 10, 1), new(2026, 10, 31), CollectionReconciliationBasis.LatestCorrected), default)).Status);
        reader.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task As_originally_known_requires_an_explicit_cutoff_and_rejects_an_unknown_basis()
    {
        var tenantId = Guid.NewGuid(); var user = Mock.Of<ICurrentUserService>(x => x.Role == "Admin" && x.IsAuthenticated && x.UserId == Guid.NewGuid() && x.MunicipalityId == tenantId);
        var reader = new Mock<ICollectionActivityReader>();
        var flow = new GetCollectionReconciliationQueryHandler(reader.Object, user,
            Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenantId), new FixedClock(new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(ResultStatus.Invalid, (await flow.Handle(new(new(2026, 10, 1), new(2026, 10, 31), CollectionReconciliationBasis.AsOriginallyKnown), default)).Status);
        Assert.Equal(ResultStatus.Invalid, (await flow.Handle(new(new(2026, 10, 1), new(2026, 10, 31), (CollectionReconciliationBasis)99), default)).Status);
        reader.VerifyNoOtherCalls();
    }
}
