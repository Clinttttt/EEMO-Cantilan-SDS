using EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassifications;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class RevenueClassificationManagementIntegrationTests(PostgresFixture db)
{
    private sealed class Caller(Guid municipalityId, string username) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => username;
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
    }

    private async Task<Guid> CreateMunicipalityAsync(string code)
    {
        var municipality = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());
        await using var context = db.CreateContext(Guid.Empty);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();
        return municipality.Id;
    }

    [SkippableFact]
    public async Task ManagementHandlersUsePostgresForTenantScopedCreateVersionHistoryAndRetirement()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("REV-MGMT-A");
        var tenantB = await CreateMunicipalityAsync("REV-MGMT-B");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var callerA = new Caller(tenantA, "head.a");
        var callerB = new Caller(tenantB, "head.b");

        Guid classificationAId;
        await using (var asA = db.CreateContext(tenantA))
        {
            var created = await new CreateRevenueClassificationCommandHandler(asA, callerA).Handle(
                new("MARKET_FEES", "Market Fees A", today, null, RevenueInstrumentType.CashTicket), default);
            Assert.True(created.IsSuccess);
            classificationAId = created.Value!.Id;
            Assert.Equal("head.a", created.Value.EffectivePolicy!.CreatedBy);

            var duplicate = await new CreateRevenueClassificationCommandHandler(asA, callerA).Handle(
                new("MARKET_FEES", "Duplicate", today, null, RevenueInstrumentType.CashTicket), default);
            Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        }

        Guid classificationBId;
        await using (var asB = db.CreateContext(tenantB))
        {
            // Identical semantic identity is valid in a different municipality.
            var created = await new CreateRevenueClassificationCommandHandler(asB, callerB).Handle(
                new("MARKET_FEES", "Market Fees B", today, null, RevenueInstrumentType.OfficialReceipt), default);
            Assert.True(created.IsSuccess);
            classificationBId = created.Value!.Id;
        }

        await using (var asA = db.CreateContext(tenantA))
        {
            var append = new AppendRevenueClassificationPolicyCommandHandler(asA, callerA);
            var nextDate = today.AddDays(1);
            var appended = await append.Handle(
                new(classificationAId, nextDate, "Market Collections A", "Future tenant wording", RevenueInstrumentType.CashTicket), default);
            Assert.True(appended.IsSuccess);

            var duplicatePolicy = await append.Handle(
                new(classificationAId, nextDate, "Duplicate date", null, RevenueInstrumentType.OfficialReceipt), default);
            Assert.Equal(ResultStatus.Conflict, duplicatePolicy.Status);

            var beforeEffectiveDate = await new GetRevenueClassificationsQueryHandler(asA, callerA, new SystemClock())
                .Handle(new(today), default);
            Assert.Equal("Market Fees A", Assert.Single(beforeEffectiveDate.Value!).EffectivePolicy!.DisplayName);

            var onEffectiveDate = await new GetRevenueClassificationsQueryHandler(asA, callerA, new SystemClock())
                .Handle(new(nextDate), default);
            Assert.Equal("Market Collections A", Assert.Single(onEffectiveDate.Value!).EffectivePolicy!.DisplayName);

            var history = await new GetRevenueClassificationPolicyHistoryQueryHandler(asA, callerA)
                .Handle(new(classificationAId), default);
            Assert.Equal(new[] { nextDate, today }, history.Value!.Select(x => x.EffectiveDate));

            var retired = await new RetireRevenueClassificationCommandHandler(asA, callerA)
                .Handle(new(classificationAId), default);
            Assert.True(retired.IsSuccess);

            var afterRetirement = await new GetRevenueClassificationsQueryHandler(asA, callerA, new SystemClock())
                .Handle(new(nextDate), default);
            var row = Assert.Single(afterRetirement.Value!);
            Assert.False(row.IsActive);
            Assert.Equal("MARKET_FEES", row.SemanticCode);
            Assert.Equal(2, (await asA.RevenueClassificationPolicies.CountAsync(x => x.RevenueClassificationId == classificationAId)));
        }

        await using var verifyB = db.CreateContext(tenantB);
        var tenantBRows = await new GetRevenueClassificationsQueryHandler(verifyB, callerB, new SystemClock())
            .Handle(new(today), default);
        var onlyB = Assert.Single(tenantBRows.Value!);
        Assert.Equal(classificationBId, onlyB.Id);
        Assert.Equal("MARKET_FEES", onlyB.SemanticCode);
        Assert.True(onlyB.IsActive);
        Assert.Equal("Market Fees B", onlyB.EffectivePolicy!.DisplayName);

        await using var outsider = db.CreateContext(tenantA);
        var crossTenantHistory = await new GetRevenueClassificationPolicyHistoryQueryHandler(outsider, callerA)
            .Handle(new(classificationBId), default);
        Assert.Equal(ResultStatus.NotFound, crossTenantHistory.Status);
    }
}
