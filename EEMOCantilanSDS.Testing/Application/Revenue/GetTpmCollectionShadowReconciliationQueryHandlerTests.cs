using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetTpmCollectionShadowReconciliation;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class GetTpmCollectionShadowReconciliationQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 9, 4);
    private static readonly DateOnly To = new(2026, 9, 25);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid? municipalityId, bool authenticated = true) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "tpm-shadow-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tpm-shadow-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static RevenueClassificationPolicy Policy(
        Guid classificationId,
        Guid tenantId,
        DateOnly effectiveDate) =>
        RevenueClassificationPolicy.Create(
            classificationId,
            effectiveDate,
            $"TABO policy {effectiveDate:yyyy-MM-dd}",
            RevenueInstrumentType.CashTicket,
            tenantId,
            createdBy: "seed");

    private static TpmAttendance AddAttendance(
        AppDbContext context,
        DateOnly marketDate,
        decimal fee,
        bool paid,
        Guid? collectorId = null)
    {
        var vendor = TpmVendor.Create($"Vendor {Guid.NewGuid():N}", "Vegetables");
        var attendance = TpmAttendance.Create(vendor.Id, marketDate, fee: fee);
        if (paid)
            attendance.MarkPaid(collectorId);

        context.AddRange(vendor, attendance);
        return attendance;
    }

    [Fact]
    public async Task UnauthenticatedOrUnresolvedCallerIsForbidden()
    {
        var options = Options();
        await using var context = Context(options, Guid.Empty);
        var callers = new ICurrentUserService[]
        {
            new Caller(Guid.NewGuid(), authenticated: false),
            new Caller(null),
            new Caller(Guid.Empty)
        };

        foreach (var caller in callers)
        {
            var result = await new GetTpmCollectionShadowReconciliationQueryHandler(context, caller)
                .Handle(new(From, To), default);

            Assert.Equal(ResultStatus.Forbidden, result.Status);
        }
    }

    [Fact]
    public async Task ValidatorRejectsInvertedRangeAndAllowsInclusiveRange()
    {
        var validator = new GetTpmCollectionShadowReconciliationQueryValidator();

        var invalid = await validator.ValidateAsync(
            new GetTpmCollectionShadowReconciliationQuery(To, From));
        Assert.False(invalid.IsValid);
        Assert.Contains(
            invalid.Errors,
            x => x.PropertyName == nameof(GetTpmCollectionShadowReconciliationQuery.To));

        Assert.True((await validator.ValidateAsync(
            new GetTpmCollectionShadowReconciliationQuery(From, From))).IsValid);
        Assert.True((await validator.ValidateAsync(
            new GetTpmCollectionShadowReconciliationQuery(From, To))).IsValid);
    }

    [Fact]
    public async Task PaidSourcesProjectByStableTaboMeaningAndBusinessDatePolicy()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var collectorId = Guid.NewGuid();

        Guid taboId;
        Guid nonTaboId;
        Guid exactPolicyId;
        Guid latestPastPolicyId;
        TpmAttendance exactDateAttendance;
        TpmAttendance latestPastAttendance;
        TpmAttendance noEffectivePolicyAttendance;
        TpmAttendance unpaidAttendance;

        await using (var seed = Context(options, tenant))
        {
            var tabo = RevenueClassification.Create(RevenueClassificationCodes.Tabo, tenant, "seed");
            tabo.Retire("seed-retired");
            var nonTabo = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant, "seed");
            taboId = tabo.Id;
            nonTaboId = nonTabo.Id;

            var olderPolicy = Policy(tabo.Id, tenant, new DateOnly(2026, 9, 11));
            var exactPolicy = Policy(tabo.Id, tenant, new DateOnly(2026, 9, 18));
            var latestPastPolicy = Policy(tabo.Id, tenant, new DateOnly(2026, 9, 20));
            var futurePolicy = Policy(tabo.Id, tenant, new DateOnly(2026, 10, 2));
            exactPolicyId = exactPolicy.Id;
            latestPastPolicyId = latestPastPolicy.Id;

            exactDateAttendance = AddAttendance(
                seed, new DateOnly(2026, 9, 18), 101.25m, paid: true, collectorId);
            latestPastAttendance = AddAttendance(
                seed, new DateOnly(2026, 9, 25), 33.33m, paid: true);
            noEffectivePolicyAttendance = AddAttendance(
                seed, new DateOnly(2026, 9, 4), 66.66m, paid: true);
            unpaidAttendance = AddAttendance(
                seed, new DateOnly(2026, 9, 11), 88.88m, paid: false);

            seed.AddRange(
                tabo,
                nonTabo,
                olderPolicy,
                exactPolicy,
                latestPastPolicy,
                futurePolicy,
                Policy(nonTabo.Id, tenant, new DateOnly(2026, 9, 1)));
            await seed.SaveChangesAsync();
        }

        await using (var seedOther = Context(options, otherTenant))
        {
            var otherTabo = RevenueClassification.Create(RevenueClassificationCodes.Tabo, otherTenant, "seed");
            seedOther.AddRange(
                otherTabo,
                Policy(otherTabo.Id, otherTenant, new DateOnly(2026, 9, 1)));
            AddAttendance(seedOther, new DateOnly(2026, 9, 25), 999.99m, paid: true);
            await seedOther.SaveChangesAsync();
        }

        await using var context = Context(options, tenant);
        var result = await new GetTpmCollectionShadowReconciliationQueryHandler(context, new Caller(tenant))
            .Handle(new(From, To), default);

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(From, reconciliation.From);
        Assert.Equal(To, reconciliation.To);
        Assert.Equal(3, reconciliation.SourceCount);
        Assert.Equal(2, reconciliation.ProjectedCount);
        Assert.Equal(1, reconciliation.UnresolvedCount);
        Assert.Equal(201.24m, reconciliation.SourceTotal);
        Assert.Equal(134.58m, reconciliation.ProjectedTotal);
        Assert.Equal(66.66m, reconciliation.UnresolvedTotal);
        Assert.Equal(66.66m, reconciliation.Difference);
        Assert.False(reconciliation.IsReconciled);
        Assert.Equal(
            reconciliation.ProjectedTotal + reconciliation.UnresolvedTotal,
            reconciliation.SourceTotal);

        var accountedIds = reconciliation.ProjectedRows.Select(x => x.TpmAttendanceId)
            .Concat(reconciliation.UnresolvedRows.Select(x => x.TpmAttendanceId))
            .OrderBy(x => x)
            .ToArray();
        var expectedIds = new[]
        {
            noEffectivePolicyAttendance.Id,
            exactDateAttendance.Id,
            latestPastAttendance.Id
        }.OrderBy(x => x).ToArray();
        Assert.Equal(expectedIds, accountedIds);
        Assert.DoesNotContain(
            reconciliation.ProjectedRows,
            x => x.TpmAttendanceId == unpaidAttendance.Id);
        Assert.DoesNotContain(
            reconciliation.UnresolvedRows,
            x => x.TpmAttendanceId == unpaidAttendance.Id);

        var exactRow = Assert.Single(
            reconciliation.ProjectedRows,
            x => x.TpmAttendanceId == exactDateAttendance.Id);
        Assert.Equal(101.25m, exactRow.Amount);
        Assert.Equal(collectorId, exactRow.CollectorId);
        Assert.Equal(taboId, exactRow.RevenueClassificationId);
        Assert.NotEqual(nonTaboId, exactRow.RevenueClassificationId);
        Assert.Equal(exactPolicyId, exactRow.RevenueClassificationPolicyId);
        Assert.Equal(new DateOnly(2026, 9, 18), exactRow.PolicyEffectiveDate);
        Assert.Equal(CollectionSourceKind.TpmAttendance, exactRow.SourceKind);
        Assert.Equal(exactDateAttendance.Id, exactRow.SourceId);

        var latestPastRow = Assert.Single(
            reconciliation.ProjectedRows,
            x => x.TpmAttendanceId == latestPastAttendance.Id);
        Assert.Equal(33.33m, latestPastRow.Amount);
        Assert.Equal(latestPastPolicyId, latestPastRow.RevenueClassificationPolicyId);
        Assert.Equal(new DateOnly(2026, 9, 20), latestPastRow.PolicyEffectiveDate);

        var unresolved = Assert.Single(
            reconciliation.UnresolvedRows,
            x => x.TpmAttendanceId == noEffectivePolicyAttendance.Id);
        Assert.Equal(66.66m, unresolved.Amount);
        Assert.Equal(
            TpmCollectionShadowUnresolvedReasons.TaboPolicyNotEffectiveCode,
            unresolved.ReasonCode);
    }

    [Fact]
    public async Task MissingTaboClassificationKeepsEligibleMoneyVisibleAsUnresolved()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        TpmAttendance attendance;

        await using (var seed = Context(options, tenant))
        {
            var ecf = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant, "seed");
            seed.AddRange(ecf, Policy(ecf.Id, tenant, new DateOnly(2026, 9, 1)));
            attendance = AddAttendance(seed, new DateOnly(2026, 9, 18), 45.75m, paid: true);
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenant);
        var result = await new GetTpmCollectionShadowReconciliationQueryHandler(context, new Caller(tenant))
            .Handle(new(From, To), default);

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(1, reconciliation.SourceCount);
        Assert.Equal(0, reconciliation.ProjectedCount);
        Assert.Equal(1, reconciliation.UnresolvedCount);
        Assert.Equal(45.75m, reconciliation.SourceTotal);
        Assert.Equal(0m, reconciliation.ProjectedTotal);
        Assert.Equal(45.75m, reconciliation.Difference);
        Assert.False(reconciliation.IsReconciled);

        var unresolved = Assert.Single(reconciliation.UnresolvedRows);
        Assert.Equal(attendance.Id, unresolved.TpmAttendanceId);
        Assert.Equal(new DateOnly(2026, 9, 18), unresolved.BusinessDate);
        Assert.Equal(45.75m, unresolved.Amount);
        Assert.Equal(
            TpmCollectionShadowUnresolvedReasons.TaboClassificationMissingCode,
            unresolved.ReasonCode);
    }
}
