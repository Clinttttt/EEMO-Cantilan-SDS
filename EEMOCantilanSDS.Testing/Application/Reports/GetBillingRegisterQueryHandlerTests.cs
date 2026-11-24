using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Queries.Reports.GetBillingRegister;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>A named month is read at its last day; no month is today; a month that is not one is refused before any read.</summary>
public class GetBillingRegisterQueryHandlerTests
{
    private static readonly BillingRegisterDto Register = new(FacilityCode.TCC, new DateOnly(2026, 2, 28), null, []);

    [Theory]
    [InlineData(2026, 2, "2026-02-28")]
    [InlineData(2024, 2, "2024-02-29")]
    [InlineData(2025, 12, "2025-12-31")]
    public async Task AMonth_IsReadAtItsLastDay(int year, int month, string lastDay)
    {
        var repo = new Mock<IFacilityReportsRepository>();
        repo.Setup(r => r.GetBillingRegisterAsync(FacilityCode.TCC, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Register);

        var result = await new GetBillingRegisterQueryHandler(repo.Object).Handle(new(FacilityCode.TCC, year, month), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.GetBillingRegisterAsync(FacilityCode.TCC, DateOnly.Parse(lastDay), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoMonth_IsToday()
    {
        var repo = new Mock<IFacilityReportsRepository>();
        repo.Setup(r => r.GetBillingRegisterAsync(FacilityCode.TCC, null, It.IsAny<CancellationToken>())).ReturnsAsync(Register);

        Assert.True((await new GetBillingRegisterQueryHandler(repo.Object).Handle(new(FacilityCode.TCC), CancellationToken.None)).IsSuccess);
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, null)]
    [InlineData(null, 5)]
    public async Task AMonthThatIsNotOne_IsRefused_BeforeAnyRead(int? year, int? month)
    {
        var repo = new Mock<IFacilityReportsRepository>(MockBehavior.Strict);

        var result = await new GetBillingRegisterQueryHandler(repo.Object).Handle(new(FacilityCode.TCC, year, month), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
