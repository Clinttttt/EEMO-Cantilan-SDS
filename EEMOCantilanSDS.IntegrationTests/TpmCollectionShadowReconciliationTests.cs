using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetTpmCollectionShadowReconciliation;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TpmCollectionShadowReconciliationTests(PostgresFixture db)
{
    private sealed record SeededSource(
        Guid AttendanceId,
        Guid ClassificationId,
        Guid PolicyId);

    private sealed class Caller(Guid municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => null;
        public string? Username => "tpm-shadow-integration-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "tpm-shadow-integration-test";
        public Guid? MunicipalityId => municipalityId;
    }

    private async Task<Guid> CreateMunicipalityAsync(string code)
    {
        var municipality = Municipality.Create(
            code,
            code,
            "Surigao del Sur",
            MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

        await using var context = db.CreateContext(Guid.Empty);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();
        return municipality.Id;
    }

    private async Task<SeededSource> SeedPaidAttendanceAsync(
        Guid tenantId,
        DateOnly date,
        decimal fee)
    {
        await using var context = db.CreateContext(tenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Tabo, tenantId);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id,
            new DateOnly(2020, 1, 1),
            "TABO policy",
            RevenueInstrumentType.CashTicket,
            tenantId);
        var vendor = TpmVendor.Create($"Vendor {Guid.NewGuid():N}", "Vegetables");
        var attendance = TpmAttendance.Create(vendor.Id, date, fee: fee);
        attendance.MarkPaid(Guid.NewGuid());

        context.AddRange(classification, policy, vendor, attendance);
        await context.SaveChangesAsync();

        return new(attendance.Id, classification.Id, policy.Id);
    }

    [SkippableFact]
    public async Task PostgreSqlShadowQueryIsTenantIsolatedAndDoesNotWriteLedger()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = await CreateMunicipalityAsync($"TPM-A-{suffix}");
        var tenantB = await CreateMunicipalityAsync($"TPM-B-{suffix}");
        var sourceA = await SeedPaidAttendanceAsync(
            tenantA, new DateOnly(2026, 9, 18), 101.25m);
        await SeedPaidAttendanceAsync(
            tenantB, new DateOnly(2026, 9, 18), 999.99m);

        await using var context = db.CreateContext(tenantA);
        var collectionsBefore = await context.Collections.CountAsync();
        var linesBefore = await context.CollectionLines.CountAsync();

        var result = await new GetTpmCollectionShadowReconciliationQueryHandler(
                context, new Caller(tenantA))
            .Handle(
                new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
                default);

        var collectionsAfter = await context.Collections.CountAsync();
        var linesAfter = await context.CollectionLines.CountAsync();

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(1, reconciliation.SourceCount);
        Assert.Equal(1, reconciliation.ProjectedCount);
        Assert.Equal(0, reconciliation.UnresolvedCount);
        Assert.Equal(101.25m, reconciliation.SourceTotal);
        Assert.Equal(101.25m, reconciliation.ProjectedTotal);
        Assert.Equal(0m, reconciliation.Difference);
        Assert.True(reconciliation.IsReconciled);

        var row = Assert.Single(reconciliation.ProjectedRows);
        Assert.Equal(sourceA.AttendanceId, row.TpmAttendanceId);
        Assert.Equal(sourceA.AttendanceId, row.SourceId);
        Assert.Equal(sourceA.ClassificationId, row.RevenueClassificationId);
        Assert.Equal(sourceA.PolicyId, row.RevenueClassificationPolicyId);
        Assert.Equal(CollectionSourceKind.TpmAttendance, row.SourceKind);

        Assert.Equal(collectionsBefore, collectionsAfter);
        Assert.Equal(linesBefore, linesAfter);
    }
}
