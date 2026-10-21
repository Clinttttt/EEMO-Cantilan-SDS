using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public sealed class RevenuePerformanceCashTests
{
    [Fact]
    public void Office_rent_activation_is_disabled_and_scoped_to_one_accepted_tenant_and_facility()
    {
        var tenant = Guid.NewGuid();
        Assert.False(new EEMOCantilanSDS.Application.Common.Revenue.MonthlyRentReceiptOptions().Allows(tenant, EEMOCantilanSDS.Domain.Enums.FacilityCode.NCC));
        var enabled = new EEMOCantilanSDS.Application.Common.Revenue.MonthlyRentReceiptOptions(true,
            new HashSet<EEMOCantilanSDS.Domain.Enums.FacilityCode> { EEMOCantilanSDS.Domain.Enums.FacilityCode.NCC }, new HashSet<Guid> { tenant });
        Assert.True(enabled.Allows(tenant, EEMOCantilanSDS.Domain.Enums.FacilityCode.NCC));
        Assert.False(enabled.Allows(Guid.NewGuid(), EEMOCantilanSDS.Domain.Enums.FacilityCode.NCC));
        Assert.False(enabled.Allows(tenant, EEMOCantilanSDS.Domain.Enums.FacilityCode.TCC));
        Assert.False(enabled.Allows(tenant, EEMOCantilanSDS.Domain.Enums.FacilityCode.NPM));
    }
    [Theory]
    [InlineData("Collector", true)]
    [InlineData("Payor", true)]
    [InlineData("Admin", false)]
    public async Task Office_rent_receipts_require_office_role_and_same_tenant(string role, bool sameTenant)
    {
        var tenant = Guid.NewGuid(); var db = new Mock<EEMOCantilanSDS.Application.Common.Interface.Persistence.IAppDbContext>(MockBehavior.Strict);
        var user = Mock.Of<EEMOCantilanSDS.Application.Common.Interface.Services.ICurrentUserService>(x => x.IsAuthenticated && x.UserId == Guid.NewGuid()
            && x.Role == role && x.MunicipalityId == (sameTenant ? tenant : Guid.NewGuid()));
        var result = await new EEMOCantilanSDS.Application.Common.Revenue.CollectionComposerWorkflow(db.Object, user,
            Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenant)).PostOfficeRentAsync(new(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, 600, 1, new(2026, 10, 18)));
        Assert.Equal(ResultStatus.Forbidden, result.Status); db.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Actual_collected_is_1200_while_official_income_and_attainment_retain_the_54_adjustment()
    {
        var tenant = Guid.NewGuid(); var accessor = Mock.Of<ICurrentMunicipalityAccessor>(x => x.MunicipalityId == tenant);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, accessor);
        var cell = new MonthlyIncomeCellDto(1200, 0, 54);
        var row = new OfficialMonthlyIncomeRowDto("RENT_NCC", "NCC", "PERMANENT_STALL_RENT", Enumerable.Repeat(cell, 12).ToArray(), cell, "Legacy", 2000, 62.7m);
        var statement = new Mock<IRequestHandler<GetOfficialMonthlyIncomeQuery, Result<OfficialMonthlyIncomeDto>>>();
        statement.Setup(x => x.Handle(It.IsAny<GetOfficialMonthlyIncomeQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(
            new(2026, 10, [new("RENT", "Rent income", [row], [], cell)], [], cell, true, [], DateTime.UtcNow)));
        var result = (await new GetRevenueSourcePerformanceQueryHandler(statement.Object, db, accessor).Handle(new(2026, 10), default)).Value!;
        Assert.Equal(1200, result.TotalCollected); Assert.Equal(1200, Assert.Single(result.Rows).Collected);
        Assert.Equal(1200, Assert.Single(result.Groups).Collected);
        Assert.Equal(54, result.Rows[0].AdjustmentAmount); Assert.Equal(62.7m, result.Rows[0].Attainment);
        Assert.Equal(1254, cell.OfficialAmount); Assert.Equal(1200, cell.SystemAmount);
        Assert.Empty(db.Collections); Assert.Empty(db.CollectionRemittances);
    }
}
