using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Services;

// The adapter between the monitoring API and the page. Fictional records only; status, expiry and counts are the DTO's.
public sealed class ReportsContractsMonitoringSourceTests
{
    private static readonly Guid Stall = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static ContractMonitoringRowDto Facility(ContractMonitoringStatus status, DateOnly? expiry = null, int? days = null,
        OccupancyArrangement? basis = OccupancyArrangement.SignedContract, int? years = 3, decimal? approved = 1500m,
        decimal? stated = 1500m, FacilityCode code = FacilityCode.NCC, Guid? stallId = null) =>
        new(ContractMonitoringSourceType.FacilityContract, Guid.NewGuid(), Guid.NewGuid(), code, code.ToString(), "Sample Centre",
            stallId ?? Stall, null, null, "Alpha Occupant", "Alpha Contractor", "S-01", basis, null,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1), years, expiry, days, status, null,
            approved, ContractMonitoringChargeFrequency.Monthly, stated, 12.5m, "Ground floor");

    private static ContractMonitoringRowDto Account_(ObligationKind kind) =>
        new(ContractMonitoringSourceType.ObligationAccount, Account, null, null, "KM", "Sample Spaces", null, kind, null,
            "Beta Holder", null, "KM-07", OccupancyArrangement.SignedContract, "DOC-9", new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 1), 3, new DateOnly(2027, 12, 31), 440, ContractMonitoringStatus.Active, null,
            900m, ContractMonitoringChargeFrequency.Monthly, 1100m, null, null);

    private static ContractsMonitoringPageDto Dto(params ContractMonitoringRowDto[] rows) =>
        new(new DateOnly(2026, 10, 14), new ContractsMonitoringSummaryDto(10, 1, 3, 2, 4, 5, 6, 7, 38), rows.Length, 1, 25, rows);

    private static string Fact(ContractsMonitoringRow r, string label) => r.Facts.Single(f => f.Label == label).Value;

    [Fact]
    public void SummaryCountsAreCopiedFromTheApiNotRecomputed()
    {
        var page = ReportsContractsMonitoringSource.ToPage(Dto(Facility(ContractMonitoringStatus.Active)), []);

        Assert.Equal(new ContractsMonitoringSummary(10, 1, 3, 2, 4, 5, 6, 7, 38), page.Summary);
        Assert.Equal(1, page.TotalRows);
    }

    [Theory]
    [InlineData(ContractMonitoringStatus.Active, "Active")]
    [InlineData(ContractMonitoringStatus.ExpiringSoon, "Expiring soon")]
    [InlineData(ContractMonitoringStatus.Expired, "Expired")]
    [InlineData(ContractMonitoringStatus.NoContract, "No contract")]
    [InlineData(ContractMonitoringStatus.Ended, "Ended")]
    [InlineData(ContractMonitoringStatus.Unknown, "Unknown")]
    [InlineData(ContractMonitoringStatus.NotYetEffective, "Not yet effective")]
    public void EveryApiStatusKeepsItsOwnWords(ContractMonitoringStatus status, string label)
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(status));
        Assert.Equal(label, row.Status);
        Assert.Equal(status.ToString(), row.StatusKey);
    }

    [Fact]
    public void ExpiryAndRemainingDaysAreTheServers()
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(ContractMonitoringStatus.ExpiringSoon, new DateOnly(2026, 10, 20), 6));
        Assert.Equal(new DateOnly(2026, 10, 20), row.Expiry);
        Assert.Equal(6, row.RemainingDays);
        Assert.Equal("3 years", row.Duration);
    }

    [Fact]
    public void SpaceOnlyHasNoTermAndIsNotPresentedAsExpired()
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(ContractMonitoringStatus.NoContract, null, null, OccupancyArrangement.SpaceOnly, null));
        Assert.Equal("No contract", row.Status);
        Assert.Equal("Space only", row.ContractBasis);
        Assert.Equal("No contract term", row.Duration);
        Assert.Null(row.Expiry);
    }

    [Fact]
    public void MissingEvidenceStaysMissingAndIsNeverFilledIn()
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(ContractMonitoringStatus.Unknown, null, null, null, null, null, null));
        Assert.Null(row.Expiry);
        Assert.Null(row.Duration);
        Assert.Null(row.ContractBasis);
        Assert.Equal("Not recorded", Fact(row, "Approved charge"));
        Assert.Equal("Not recorded", Fact(row, "Duration"));
        Assert.DoesNotContain(row.Facts, f => f.Label == "Amount stated in contract");
    }

    [Fact]
    public void ApprovedChargeAndContractStatedAmountStayDistinct()
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(ContractMonitoringStatus.Active, approved: 1500m, stated: 1200m));
        Assert.Equal("₱1,500.00 / month", Fact(row, "Approved charge"));
        Assert.Equal("₱1,200.00 / month", Fact(row, "Amount stated in contract"));
    }

    [Fact]
    public void AnEndedRowWithoutRecordedClosureSaysSo()
    {
        var row = ReportsContractsMonitoringSource.ToRow(Facility(ContractMonitoringStatus.Ended));
        Assert.Equal("Not recorded", Fact(row, "Closed on"));
    }

    [Fact]
    public void RowIdentityIsTheSourceTypeAndRecordIdNotAName()
    {
        var a = Facility(ContractMonitoringStatus.Active);
        var b = a with { SourceRecordId = Guid.NewGuid() };
        var rows = ReportsContractsMonitoringSource.ToPage(Dto(a, b), []).Rows;
        Assert.Equal(rows[0].Occupant, rows[1].Occupant);
        Assert.NotEqual(rows[0].Id, rows[1].Id);
        Assert.Equal($"FacilityContract:{a.SourceRecordId}", rows[0].Id);
    }

    [Fact]
    public void SourceRoutesExistOnlyWhereThePortalHasADirectRecordPage()
    {
        Assert.Equal($"/profile/ncc/{Stall}", ReportsContractsMonitoringSource.SourceHref(Facility(ContractMonitoringStatus.Active)));
        Assert.Equal($"/profile/custom1/{Stall}", ReportsContractsMonitoringSource.SourceHref(Facility(ContractMonitoringStatus.Active, code: FacilityCode.Custom1)));
        Assert.Equal($"/operations/kanmanggay/profile/{Account}", ReportsContractsMonitoringSource.SourceHref(Account_(ObligationKind.KanmanggaySpaceRental)));
        Assert.Equal($"/operations/vegetable-fruit/profile/{Account}", ReportsContractsMonitoringSource.SourceHref(Account_(ObligationKind.VegetableFruitSpaceRental)));
        Assert.Null(ReportsContractsMonitoringSource.SourceHref(Account_(ObligationKind.FiestaArawLotRental)));
        Assert.Null(ReportsContractsMonitoringSource.SourceHref(Facility(ContractMonitoringStatus.Active) with { StallId = null }));
    }

    [Fact]
    public void FiltersBecomeTheCanonicalApiRequest()
    {
        var request = ReportsContractsMonitoringSource.ToRequest(new ContractsMonitoringQuery("  alpha ", "KM", "ExpiringSoon", 30, "SignedContract", 3, 25));
        Assert.Equal("alpha", request.Search);
        Assert.Equal("KM", request.Operation);
        Assert.Equal("ExpiringSoon", request.Status);
        Assert.Equal(30, request.ExpiryWindow);
        Assert.Equal("SignedContract", request.OccupancyBasis);
        Assert.Equal(3, request.Page);
        Assert.Equal(25, request.PageSize);
        Assert.Null(request.AsOf);   // the API serves today only
        Assert.Null(ReportsContractsMonitoringSource.ToRequest(new ContractsMonitoringQuery()).Search);
    }

    private static ReportsContractsMonitoringSource Source(Mock<IReportsApiClient> reports)
    {
        var facilities = new Mock<IFacilitiesApiClient>();
        var settings = new Mock<IOperationSettingsApiClient>();
        settings.Setup(s => s.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Failure("n/a", 500));
        return new ReportsContractsMonitoringSource(reports.Object, new FacilityState(facilities.Object), new OperationNameService(settings.Object));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AuthorizationFailuresAreReportedAsUnauthorized(int status)
    {
        var reports = new Mock<IReportsApiClient>();
        reports.Setup(r => r.GetContractsMonitoringAsync(It.IsAny<ContractsMonitoringRequest>()))
            .ReturnsAsync(Result<ContractsMonitoringPageDto>.Failure("no", status));

        var result = await Source(reports).LoadAsync(new ContractsMonitoringQuery());

        Assert.Null(result.Page);
        Assert.Equal(ContractsMonitoringFailure.Unauthorized, result.Failure);
    }

    [Fact]
    public async Task ServerFailureIsAnErrorThatCanBeRetried()
    {
        var reports = new Mock<IReportsApiClient>();
        reports.Setup(r => r.GetContractsMonitoringAsync(It.IsAny<ContractsMonitoringRequest>()))
            .ReturnsAsync(Result<ContractsMonitoringPageDto>.Failure("boom", 500));

        var result = await Source(reports).LoadAsync(new ContractsMonitoringQuery());

        Assert.Equal(ContractsMonitoringFailure.Error, result.Failure);
    }

    [Fact]
    public async Task SuccessOffersOperationChoicesFromTheCatalogNotFromTheRows()
    {
        var reports = new Mock<IReportsApiClient>();
        reports.Setup(r => r.GetContractsMonitoringAsync(It.IsAny<ContractsMonitoringRequest>()))
            .ReturnsAsync(Result<ContractsMonitoringPageDto>.Success(Dto()));

        var result = await Source(reports).LoadAsync(new ContractsMonitoringQuery());

        var values = result.Page!.Operations.Select(o => o.Value).ToList();
        Assert.Contains("KM", values);
        Assert.Contains("VF", values);
        Assert.DoesNotContain("SLH", values);
        Assert.Empty(result.Page.Rows);
    }

    [Fact]
    public async Task ACancelledRequestDoesNotCallTheApi()
    {
        var reports = new Mock<IReportsApiClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source(reports).LoadAsync(new ContractsMonitoringQuery(), cts.Token));

        reports.Verify(r => r.GetContractsMonitoringAsync(It.IsAny<ContractsMonitoringRequest>()), Times.Never);
    }
}
