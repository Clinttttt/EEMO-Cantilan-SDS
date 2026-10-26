using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// ST-20. The Head-controlled, PROSPECTIVE route that makes a facility's FRESH monthly rent rows collectible with a genuine SRC. A monthly
/// facility (ICE, BBQ, TCC, NCC, custom Stall Rent / Space Rental) collects on the legacy PaymentRecord path until a row's settlement authority
/// is Canonical; only then does Mobile (and the office) post a canonical Collection and return an SRC. This is the existing source-scoped cutover
/// (<see cref="SettlementCutoverWorkflow"/>: Legacy, Pending Cutover, frozen opening position, Canonical) applied to one facility-month at a time
/// to rows that carry no legacy settlement at all.
/// </summary>
/// <remarks>
/// It never converts history. A row with any legacy settlement evidence (a payment, a partial, an OR number, mixed utility/fish components), a row
/// already Canonical, an excused month and a month the occupancy does not bill are named and left exactly as they are. It never writes authority
/// state itself: every step is the cutover workflow's own command, each in its own serializable transaction with its own version check, and the whole
/// facility-month is refused, writing nothing, while any blocker (including each affected collector's device-version and drained-queue evidence)
/// remains. Missing month rows are created Unpaid, exactly what the legacy path would create at the first collection, only inside the same
/// transaction that proves readiness. Activation is bound to the reviewed plan's fingerprint, and a retry resumes whichever step a row reached.
/// No Collection, SRC or revenue is created here; the first SRC comes from the first real collection afterwards.
/// </remarks>
public sealed class RentActivationWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    SettlementCutoverWorkflow cutover,
    IClock? clock = null)
{
    private const string LegacyMarker = "Mark this exact source Pending Cutover before quiescing its legacy writers.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateOnly Today => clock?.PhilippineToday ?? PhilippineTime.Today;

    private static SettlementCutoverScope ScopeOf(Guid recordId) => new(CollectionSourceKind.PaymentRecord, recordId, null);

    private sealed record Candidate(Stall Stall, string Occupant, PaymentRecord? Record, bool Eligible, string? Reason, decimal Rent);

    public async Task<Result<RentActivationPlanDto>> PlanAsync(RentActivationPlanRequest request, CancellationToken ct = default)
    {
        if (Problem(request.Facility, request.Year, request.Month) is { } problem)
            return Result<RentActivationPlanDto>.Failure(problem.Message, problem.Status);
        var (plan, _) = await BuildAsync(municipality.MunicipalityId, request.Facility, request.Year, request.Month, request.Evidence, ct);
        return Result<RentActivationPlanDto>.Success(plan);
    }

    public async Task<Result<RentActivationResultDto>> ActivateAsync(RentActivationRequest request, CancellationToken ct = default)
    {
        if (request.Evidence is null)
            return Result<RentActivationResultDto>.Failure("The attested reconciliation checklist is required.", ResultStatus.Invalid);
        if (Problem(request.Facility, request.Year, request.Month) is { } problem)
            return Result<RentActivationResultDto>.Failure(problem.Message, problem.Status);
        var tenantId = municipality.MunicipalityId;

        var (plan, candidates) = await BuildAsync(tenantId, request.Facility, request.Year, request.Month, request.Evidence, ct);
        if (!string.Equals(plan.PlanFingerprint, request.ExpectedPlanFingerprint, StringComparison.Ordinal))
            return Result<RentActivationResultDto>.Failure("The reviewed rows or evidence changed. Review the plan again before activating.", ResultStatus.Conflict);
        var rows = candidates.Where(c => c.Eligible).ToList();
        if (rows.Count == 0)
            return Result<RentActivationResultDto>.Failure("There is no fresh unpaid row to activate for this facility and month.", ResultStatus.Conflict);

        // Phase 1 - one transaction: create the missing Unpaid rows and prove every row ready. A blocker rolls it all back, so nothing is written.
        var actorName = currentUser.Username ?? "Office";
        var scopes = new List<(Guid StallId, Guid RecordId)>();
        try
        {
            await using var transaction = await db.BeginSerializableTransactionAsync(ct);
            foreach (var row in rows)
            {
                var record = row.Record;
                if (record is null)
                {
                    var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(tenantId, row.Stall.Id, request.Year, request.Month,
                        Today, tracked: true, materializeMissingSource: true, actor: actorName, ct: ct);
                    record = facts?.Record ?? throw new InvalidOperationException("The month row could not be created for " + row.Stall.StallNo + ".");
                }
                scopes.Add((row.Stall.Id, record.Id));
            }
            await db.SaveChangesAsync(ct);

            var blockers = new List<string>();
            foreach (var (stallId, recordId) in scopes)
            {
                var readiness = await cutover.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(ScopeOf(recordId), request.Evidence), ct);
                if (!readiness.IsSuccess || readiness.Value is null)
                    blockers.Add(readiness.Error ?? "Readiness could not be evaluated.");
                else blockers.AddRange(readiness.Value.BlockingReasons.Where(b => b != LegacyMarker));
            }
            if (blockers.Count > 0)
                return Result<RentActivationResultDto>.Failure("Activation is blocked: " + string.Join("; ", blockers.Distinct()), ResultStatus.Conflict);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Result<RentActivationResultDto>.Failure("A month row was created concurrently. Review the plan again.", ResultStatus.Conflict);
        }
        catch (InvalidOperationException ex)
        {
            db.ChangeTracker.Clear();
            return Result<RentActivationResultDto>.Failure(ex.Message, ResultStatus.Conflict);
        }

        // Phase 2 - each row through the cutover workflow's own commands; a failure stops here and a retry resumes from the step each row reached.
        var results = new List<RentActivationRowDto>();
        int activated = 0, failed = 0;
        foreach (var row in rows)
        {
            var recordId = scopes.Single(s => s.StallId == row.Stall.Id).RecordId;
            var outcome = await ActivateRowAsync(recordId, request.Evidence, ct);
            if (outcome is null) { activated++; results.Add(Row(row, recordId, SettlementAuthority.Canonical, true, null, [])); }
            else { failed++; results.Add(Row(row, recordId, null, false, outcome, [outcome])); break; }
        }
        var message = failed == 0 ? null : $"{activated} activated, {failed} stopped. Review the plan and activate again to resume.";
        return Result<RentActivationResultDto>.Success(new(request.Facility, request.Year, request.Month, activated, failed, results, message));
    }

    private async Task<string?> ActivateRowAsync(Guid recordId, SettlementCutoverReconciliationEvidence evidence, CancellationToken ct)
    {
        var scope = ScopeOf(recordId);
        var record = await db.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == recordId, ct);
        if (record.SettlementAuthorityState == SettlementAuthority.Canonical) return null;
        if (record.SettlementAuthorityState == SettlementAuthority.Legacy)
        {
            var begun = await cutover.BeginPendingCutoverAsync(scope, ct);
            if (!begun.IsSuccess) return begun.Error ?? "The row could not enter Pending Cutover.";
        }
        var frozen = await db.CollectionSettlementCutovers.AsNoTracking().AnyAsync(x =>
            x.SourceKind == CollectionSourceKind.PaymentRecord && x.SourceId == recordId && x.SourcePart == null, ct);
        if (!frozen)
        {
            var readiness = await cutover.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence), ct);
            if (!readiness.IsSuccess || readiness.Value is null) return readiness.Error ?? "Readiness could not be evaluated.";
            var freeze = await cutover.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
                scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, evidence), ct);
            if (!freeze.IsSuccess) return freeze.Error ?? "The opening position could not be frozen.";
        }
        var current = await db.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == recordId, ct);
        var activated = await cutover.ActivateCanonicalAsync(scope, current.SettlementVersion, ct);
        return activated.IsSuccess ? null : activated.Error ?? "The row could not be activated.";
    }

    private async Task<(RentActivationPlanDto Plan, List<Candidate> Candidates)> BuildAsync(
        Guid tenantId, FacilityCode facilityCode, int year, int month, SettlementCutoverReconciliationEvidence? evidence, CancellationToken ct)
    {
        var today = Today;
        var stalls = await db.Stalls.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.Facility!.Code == facilityCode)
            .Include(x => x.Facility).Include(x => x.Contracts)
            .OrderBy(x => x.StallNo).ToListAsync(ct);
        var ids = stalls.Select(x => x.Id).ToArray();
        var records = await db.PaymentRecords.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.StallId) && x.BillingYear == year && x.BillingMonth == month)
            .ToDictionaryAsync(x => x.StallId, ct);
        var excused = (await db.StallMonthlyExceptions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.StallId) && x.BillingYear == year && x.BillingMonth == month)
            .Select(x => x.StallId).ToListAsync(ct)).ToHashSet();

        var candidates = new List<Candidate>();
        foreach (var stall in stalls)
        {
            var occupancy = stall.OccupancyAnsweringForMonth(year, month, today);
            if (occupancy is null || !occupancy.Contract.BillsCalendarMonth(year, month)) continue;   // the month is not billed to anyone: not part of this review
            records.TryGetValue(stall.Id, out var record);
            var rent = occupancy.Contract.MonthlyRentalRate > 0m ? occupancy.Contract.MonthlyRentalRate : stall.MonthlyRate;
            string? reason = null;
            if (excused.Contains(stall.Id)) reason = "This month is excused for the stall.";
            else if (stall.Status != StallStatus.Active) reason = "The stall is not active.";
            else if (record is null) { if (rent <= 0m) reason = "The occupancy has no approved rental amount."; }
            else if (record.SettlementAuthorityState == SettlementAuthority.Canonical) reason = "Already canonical.";
            else if (record.Status != PaymentStatus.Unpaid || record.PartialAmount != 0m || record.PaidAt is not null
                || !string.IsNullOrWhiteSpace(record.ORNumber)
                || record.ElecAmount.GetValueOrDefault() != 0m || record.WaterAmount.GetValueOrDefault() != 0m || record.FishFeeAmount.GetValueOrDefault() != 0m)
                reason = "Carries legacy settlement evidence; it stays on its legacy path.";
            candidates.Add(new(stall, occupancy.Contract.ActualOccupant, record, reason is null, reason, rent));
        }

        var rows = new List<RentActivationRowDto>();
        var facilityBlockers = new List<string>();
        foreach (var c in candidates)
        {
            var blockers = new List<string>();
            if (c.Eligible && c.Record is { } existing)
            {
                var readiness = await cutover.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(ScopeOf(existing.Id), evidence), ct);
                if (readiness.IsSuccess && readiness.Value is not null)
                    blockers.AddRange(readiness.Value.BlockingReasons.Where(b => b != LegacyMarker));
                else blockers.Add(readiness.Error ?? "Readiness could not be evaluated.");
            }
            rows.Add(Row(c, c.Record?.Id, c.Record?.SettlementAuthorityState, c.Eligible, c.Reason, blockers));
        }
        if (candidates.Any(c => c.Eligible && c.Record is null))
            facilityBlockers.Add("Rows that do not exist yet are created Unpaid and proven ready inside the activation itself; a blocker found there rolls everything back.");
        if (evidence is null) facilityBlockers.Add("The attested reconciliation checklist is required to activate.");

        var eligibleRows = rows.Count(r => r.Eligible);
        var ready = eligibleRows > 0 && evidence is not null && rows.All(r => !r.Eligible || r.Blockers.Count == 0);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            tenant = tenantId, facilityCode, year, month,
            rows = rows.Select(r => new { r.StallId, r.PaymentRecordId, r.Authority, r.Eligible, r.Reason, r.Blockers }).ToArray(),
            evidence = evidence is null ? null : new
            {
                evidence.LegacyWritersQuiesced, evidence.MobileQueuesDrained, evidence.NoUnregisteredFieldDevices, evidence.OnlinePaymentsDrained,
                evidence.AccountableDocumentInventoryReconciled, evidence.ReportingPathVerified, evidence.EvidenceReference,
                Collectors = (evidence.CollectorEvidence ?? []).OrderBy(x => x.CollectorId).ToArray()
            }
        }, Json))));
        return (new RentActivationPlanDto(facilityCode, year, month, rows, eligibleRows, rows.Count - eligibleRows, facilityBlockers, ready, fingerprint), candidates);
    }

    private static RentActivationRowDto Row(Candidate c, Guid? recordId, SettlementAuthority? authority, bool eligible, string? reason, IReadOnlyList<string> blockers) =>
        new(c.Stall.Id, c.Stall.StallNo, c.Occupant, recordId, authority, eligible, reason, blockers);

    /// <summary>Only a Head/Administrator, only a monthly-rental facility that is not the daily market, and only a month that has begun.</summary>
    private (string Message, ResultStatus Status)? Problem(FacilityCode facility, int year, int month)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is not ("Admin" or "SuperAdmin"))
            return ("Only the Head or an Administrator can activate monthly rent for canonical collection.", ResultStatus.Forbidden);
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return ("Only the Head or an Administrator can activate monthly rent for canonical collection.", ResultStatus.Forbidden);
        if (facility == FacilityCode.NPM)
            return ("The Public Market has its own month activation; this route covers the other monthly-rent facilities.", ResultStatus.Invalid);
        if (!MonthlyRentalFacilities.Codes.Contains(facility))
            return ("This facility is not billed as a flat monthly rental.", ResultStatus.Invalid);
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return ("Choose a valid billing year and month.", ResultStatus.Invalid);
        if (new DateOnly(year, month, 1) > Today)
            return ("A month that has not begun cannot be activated.", ResultStatus.Invalid);
        return null;
    }
}
