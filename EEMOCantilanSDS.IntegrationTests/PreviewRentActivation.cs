using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Preview-only: prepares one fresh synthetic monthly-rent source and takes it through the REAL
/// Begin -> Freeze -> Activate cutover lifecycle. It writes no authority state, evidence or collection itself, never
/// collects cash, and refuses (throws) on any identity or source doubt. Used by the disposable-PostgreSQL tests and,
/// behind an explicit environment gate, by the one authorized localhost preview.
/// </summary>
public static class PreviewRentActivation
{
    public const string StallNumber = PreviewMonthlyRentGate.SyntheticStallPrefix + "NCC-01";
    public const decimal MonthlyRate = 1200m;

    public sealed record Outcome(Guid StallId, Guid ContractId, Guid PaymentRecordId, long SourceVersion, SettlementAuthority Authority);

    private sealed class Actor(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "preview-activation";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "preview";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    public static async Task<Outcome> RunAsync(
        AppDbContext context, PreviewMonthlyRentTarget target, string connectionString, Guid actorUserId,
        int year, int month, string stallNumber = StallNumber, CancellationToken ct = default)
    {
        if (!PreviewMonthlyRentGate.PermittedStalls.Contains(stallNumber))
            throw new InvalidOperationException("Refused: that synthetic stall identity is not permitted.");
        var refusals = new List<string>(PreviewMonthlyRentGate.DatabaseRefusals(connectionString, target.Database, target.Port));
        var actual = await context.Database.SqlQuery<string>($"select current_database() as \"Value\"").SingleAsync(ct);
        if (!string.Equals(actual, target.Database, StringComparison.Ordinal))
            refusals.Add("The live session is not connected to the named preview database.");
        if (!await context.Municipalities.AnyAsync(x => x.Id == target.Tenant, ct))
            refusals.Add("The reviewed tenant does not exist in this database.");
        Refuse(refusals);

        var facility = await context.Facilities.SingleOrDefaultAsync(x => x.MunicipalityId == target.Tenant && x.Code == target.Facility, ct)
            ?? throw new InvalidOperationException("Refused: the reviewed facility does not exist for this tenant.");
        // A Public Market qualifies only as a declared monthly-rental market; a daily-collected market (Cantilan) is never a monthly source.
        if (facility.Code == FacilityCode.NPM && !facility.IsMonthlyRentMarket)
            throw new InvalidOperationException("Refused: this Public Market is collected daily, not declared as monthly rental.");

        var stall = await context.Stalls.Include(x => x.Contracts)
            .SingleOrDefaultAsync(x => x.FacilityId == facility.Id && x.StallNo == stallNumber, ct);
        if (stall is null)
        {
            // Normal domain construction, exactly as seeding a stall and its contract: an approved flat rate and an
            // unpaid month that has no payment, no cutover and no Collection.
            stall = Stall.Create(facility.Id, stallNumber, MonthlyRate, ApplicableFees.BaseRental, municipalityId: target.Tenant);
            var contract = Contract.Create(stall.Id, "Preview Synthetic Occupant", "Preview Synthetic Occupant",
                new DateOnly(year, month, 1), 5, MonthlyRate, createdBy: "preview-activation");
            context.AddRange(stall, contract);
            context.PaymentRecords.Add(PaymentRecord.Create(stall.Id, year, month, MonthlyRate, "preview-activation"));
            await context.SaveChangesAsync(ct);
            stall = await context.Stalls.Include(x => x.Contracts).SingleAsync(x => x.Id == stall.Id, ct);
        }

        var record = await context.PaymentRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StallId == stall.Id && x.BillingYear == year && x.BillingMonth == month, ct)
            ?? throw new InvalidOperationException("Refused: the synthetic month has no source record.");
        var hasCutover = await context.CollectionSettlementCutovers.AnyAsync(x => x.SourceId == record.Id, ct);
        var hasCollection = await context.CollectionAllocations.AnyAsync(x => x.SourceId == record.Id, ct);
        Refuse(PreviewMonthlyRentGate.SourceRefusals(target, stall.MunicipalityId, facility.Code, stall.StallNo,
            record.SettlementAuthorityState, record.Status, record.PartialAmount, hasCutover, hasCollection));

        // The workflow demands attestations a person would make; only attest what the database objectively shows.
        var collectors = await context.CollectorFacilityAssignments.CountAsync(x => x.FacilityId == facility.Id, ct);
        var online = await context.OnlinePaymentTransactions.CountAsync(x => x.PaymentRecordId == record.Id, ct);
        if (collectors > 0 || online > 0)
            throw new InvalidOperationException("Refused: collectors are assigned to this facility or online payments exist, so queue-drain evidence cannot be attested by a tool.");

        var workflow = new SettlementCutoverWorkflow(context, new Actor(actorUserId, target.Tenant), new Tenant(target.Tenant));
        var scope = new SettlementCutoverScope(CollectionSourceKind.PaymentRecord, record.Id, null);
        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            $"PREVIEW-SYNTHETIC {DateTime.UtcNow:yyyyMMdd}: fresh stall {stallNumber}, zero legacy cash, no collectors assigned, no online payments",
            []);

        var begun = await workflow.BeginPendingCutoverAsync(scope, ct);
        if (!begun.IsSuccess) throw new InvalidOperationException("Begin refused: " + begun.Error);
        var readiness = await workflow.EvaluateReadinessAsync(new(scope, evidence), ct);
        if (!readiness.IsSuccess || !readiness.Value!.Ready)
            throw new InvalidOperationException("Readiness blocked (source left at Pending Cutover): " + (readiness.Error ?? string.Join("; ", readiness.Value!.BlockingReasons)));
        var frozen = await workflow.FreezeOpeningPositionAsync(
            new(scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, evidence), ct);
        if (!frozen.IsSuccess) throw new InvalidOperationException("Freeze refused (source left at Pending Cutover): " + frozen.Error);
        var activated = await workflow.ActivateCanonicalAsync(scope, frozen.Value!.SourceVersion, ct);
        if (!activated.IsSuccess) throw new InvalidOperationException("Activation refused (source left at Pending Cutover): " + activated.Error);

        var contractId = stall.Contracts.OrderByDescending(x => x.CreatedAt).First().Id;
        return new Outcome(stall.Id, contractId, record.Id, activated.Value!.SourceVersion, activated.Value.Authority);
    }

    private static void Refuse(IReadOnlyCollection<string> refusals)
    {
        if (refusals.Count > 0) throw new InvalidOperationException("Refused: " + string.Join(" ", refusals));
    }
}
