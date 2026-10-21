using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    private const string FullOpeningMonthRule = "ApprovedFullOpeningMonthV1";

    /// <summary>Server-owned rate preview, or the original prepared evidence. Never reprices an existing source.</summary>
    public async Task<Result<NpmOpeningMonthAssessmentReview>> ReviewOpeningMonthAsync(Guid stallId, Guid contractId,
        DateOnly period, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role != "SuperAdmin") return Result<NpmOpeningMonthAssessmentReview>.Forbidden();
        try
        {
            var source = await db.NpmRentMonths.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant
                && x.StallId == stallId && x.PeriodStart == period, ct);
            if (source is not null)
            {
                if (source.ContractId != contractId || OpeningEvidence(source) is not { } authority)
                    throw new InvalidOperationException("An existing assessment belongs to another owner or policy. Review its original source.");
                return Result<NpmOpeningMonthAssessmentReview>.Success(new(source.Id, source.SettlementVersion,
                    source.AssessedAmount, source.CreditedAmount, authority, source.CanAcceptCollections));
            }
            var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
            var contract = stall?.Contracts.SingleOrDefault(x => x.MunicipalityId == Tenant && x.Id == contractId);
            if (stall is null || contract is null) return Result<NpmOpeningMonthAssessmentReview>.NotFound();
            var proposed = await ResolveOpeningAssessmentAsync(stall, contract, period, ct);
            return Result<NpmOpeningMonthAssessmentReview>.Success(new(null, null, proposed.AssessedAmount, proposed.CreditedAmount,
                JsonSerializer.Deserialize<NpmOpeningMonthRateEvidence>(proposed.RateEvidenceJson, Json)!));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        { return Result<NpmOpeningMonthAssessmentReview>.Failure("OFFICE_REVIEW_REQUIRED: " + ex.Message, ResultStatus.Conflict); }
    }

    /// <summary>
    /// A Head's authenticated registration is the authorization event when the tenant has already
    /// approved this opening-month policy. Runs inside the caller's registration transaction;
    /// failure rolls back the occupancy as well as the assessment. Creates no cash or receipt.
    /// Older accounts and unsupported arrangements continue on their original rent path.
    /// </summary>
    public async Task<Result<bool>> ActivateRegisteredOpeningAsync(Guid stallId, Guid contractId,
        DateOnly period, CancellationToken ct = default)
    {
        if (!Enabled || !await MustUseMonthlyAuthorityAsync(stallId, period, ct))
            return Result<bool>.Success(false);
        if (user.Role != "SuperAdmin")
            return Result<bool>.Failure("A Head must register this newly approved monthly rental.", ResultStatus.Forbidden);

        var review = await ReviewOpeningMonthAsync(stallId, contractId, period, ct);
        if (!review.IsSuccess)
            return Result<bool>.Failure(review.Error!, review.Status);
        if (review.Value!.IsActivated)
            return Result<bool>.Success(true);
        // A previously prepared financial source is not a virgin registration; never
        // erase or automatically authorize its pending historical evidence.
        if (review.Value.MonthId is not null)
            return Result<bool>.Failure("This rental already has a prepared assessment. A Head must review that source.", ResultStatus.Conflict);

        var prepared = await PrepareOpeningMonthAsync(stallId, contractId, period, ct);
        if (!prepared.IsSuccess)
            return Result<bool>.Failure(prepared.Error!, prepared.Status);
        var refreshed = await ReviewOpeningMonthAsync(stallId, contractId, period, ct);
        if (!refreshed.IsSuccess || refreshed.Value?.Version is not { } version
            || refreshed.Value.GrossObligation != review.Value.GrossObligation
            || refreshed.Value.Credits != review.Value.Credits
            || refreshed.Value.Authority != review.Value.Authority)
            return Result<bool>.Failure("Opening assessment changed during registration. Nothing was authorized.", ResultStatus.Conflict);

        // The existing signed tenant policy is the rate decision; the authenticated Head
        // is the responsible registrant. Do not invent a new official approval reference.
        var decision = $"AUTO-REGISTRATION:{contractId:N}:{review.Value.Authority.ApprovalReference}";
        if (decision.Length > 1000) decision = decision[..1000];
        var activated = await ActivateOpeningMonthAsync(
            new(stallId, contractId, period, version, decision), ct);
        return activated.IsSuccess ? Result<bool>.Success(true)
            : Result<bool>.Failure(activated.Error!, activated.Status);
    }

    /// <summary>Only virgin prospective openings may use the server-created empty historical manifest.</summary>
    public async Task<Result<long>> ActivateOpeningMonthAsync(NpmOpeningMonthActivationRequest request,
        CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role != "SuperAdmin") return Result<long>.Forbidden();
        if (string.IsNullOrWhiteSpace(request.DecisionReference) || request.DecisionReference.Length > 1000)
            return Result<long>.Failure("A Head activation reference of at most 1,000 characters is required.");
        var review = await ReviewOpeningMonthAsync(request.StallId, request.ContractId, request.PeriodStart, ct);
        if (!review.IsSuccess) return Result<long>.Failure(review.Error!, review.Status);
        if (review.Value!.MonthId is null || review.Value.Version != request.ExpectedVersion || review.Value.IsActivated)
            return Result<long>.Failure("The assessment changed or is already activated. Refresh its current position.", ResultStatus.Conflict);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("[]")));
        return await ReviewAndActivateAsync(request.StallId, request.PeriodStart, request.ExpectedVersion,
            new(1, [], [], fingerprint), request.DecisionReference.Trim(), ct);
    }

    /// <summary>Manual preparation for exceptional reviewed cases; normal approved Head registration uses ActivateRegisteredOpeningAsync.</summary>
    public async Task<Result<Guid>> PrepareOpeningMonthAsync(Guid stallId, Guid contractId, DateOnly period,
        CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role != "SuperAdmin") return Result<Guid>.Forbidden();
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        try
        {
            if (await db.NpmRentMonths.AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId && x.PeriodStart == period, ct))
                throw new InvalidOperationException("A month is already assessed; review its original evidence without repricing it.");
            var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
            var contract = stall?.Contracts.SingleOrDefault(x => x.MunicipalityId == Tenant && x.Id == contractId);
            if (stall is null || contract is null) return Result<Guid>.NotFound();
            var evidence = await ResolveOpeningAssessmentAsync(stall, contract, period, ct);
            var prepared = NpmRentMonth.Prepare(stall.Facility!, stall, contract, period, clock.PhilippineToday,
                evidence, user.UserId!.Value, user.Username!, user.Role!, clock.UtcNow);
            db.NpmRentMonths.Add(prepared.Month); db.AuditLogs.Add(prepared.Audit);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Result<Guid>.Success(prepared.Month.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            db.ChangeTracker.Clear();
            return Result<Guid>.Failure("OFFICE_REVIEW_REQUIRED: Opening assessment was not confirmed. " + ex.Message, ResultStatus.Conflict);
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<Guid>.Failure("Opening assessment outcome was not confirmed. Read the existing month source before retrying; no automatic retry is performed.", ResultStatus.Conflict);
        }
    }

    private async Task<NpmRentMonthAssessmentEvidence> ResolveOpeningAssessmentAsync(Stall stall, Contract contract,
        DateOnly period, CancellationToken ct)
    {
        if (period.Day != 1 || period.Year is < 2000 or > 2100 || stall.MunicipalityId != Tenant
            || contract.MunicipalityId != Tenant || contract.StallId != stall.Id
            || stall.Facility is not { Code: FacilityCode.NPM, IsDeleted: false, IsMonthlyRentMarket: false } facility
            || facility.MunicipalityId != Tenant || facility.MonthBasis != NpmMonthBasis.RentGoal
            || stall.IsDeleted || stall.Status != StallStatus.Active || contract.IsDeleted || !contract.IsActive
            || contract.EffectivityDate < period || contract.EffectivityDate >= period.AddMonths(1)
            || contract.EffectivityDate > clock.PhilippineToday || contract.CreatedAt > clock.UtcNow || stall.IsCustomSection)
            throw new InvalidOperationException("A current RentGoal opening with verified monthly rate coverage is required; custom-section terms need separate monthly authority.");
        var policy = (openingPolicies ?? new()).Require(Tenant, facility.Id, contract.EffectivityDate, contract.CreatedAt, clock.UtcNow);
        var end = period.AddMonths(1).AddDays(-1);
        var windows = stall.Occupancies(clock.PhilippineToday).Where(x => x.Overlaps(period, end)).ToArray();
        if (windows.Length != 1 || windows[0].Contract.Id != contract.Id || windows[0].BillableEnd < end)
            throw new InvalidOperationException("A shared, terminated or handover month needs an approved liability allocation; two holders cannot each be charged a full month.");
        if (await db.DailyCollections.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stall.Id
                && x.CollectionDate >= period && x.CollectionDate <= end, ct)
            || await db.PaymentRecords.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stall.Id
                && x.BillingYear == period.Year && x.BillingMonth == period.Month, ct))
            throw new InvalidOperationException("Existing daily or monthly assessment/payment evidence cannot be converted prospectively.");
        var rateDate = contract.EffectivityDate;
        var snapshot = await rates.GetSnapshotAsync(ct);
        var monthly = snapshot.ResolveEntryOrNull(FeeRateKey.NpmMonthlyStall, rateDate);
        var daily = NpmDailyFee.ForStallOrNull(stall, snapshot, rateDate);
        if (!snapshot.MonthRule.HasMonthlyGoal || monthly is not { Amount: > 0 } || daily is not > 0
            || decimal.Round(monthly.Value.Amount, 2) != monthly.Value.Amount || decimal.Round(daily.Value, 2) != daily.Value)
            throw new InvalidOperationException("An explicit approved monthly rate and daily installment are required; form amounts and daily-times-thirty are not monthly approval.");
        var rateRows = await db.FacilityRates.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && x.FacilityCode == FacilityCode.NPM && x.RateKey == FeeRateKey.NpmMonthlyStall && x.EffectiveDate == monthly.Value.EffectiveDate).ToListAsync(ct);
        if (rateRows.Count != 1 || rateRows[0].Amount != monthly.Value.Amount || string.IsNullOrWhiteSpace(rateRows[0].CreatedBy))
            throw new InvalidOperationException("The resolved monthly rate must match one recorded tenant-owned approval row.");
        var row = rateRows[0];
        var frozen = new NpmOpeningMonthRateEvidence(FullOpeningMonthRule, Tenant, facility.Id, stall.Id, contract.Id,
            rateDate, rateDate, row.Id, row.EffectiveDate, row.Amount, daily.Value, stall.MonthlyRate,
            contract.MonthlyRentalRate, row.UpdatedBy ?? row.CreatedBy!, row.UpdatedAt ?? row.CreatedAt,
            policy.ApprovedByUserId, policy.ApprovedAtUtc, policy.EffectiveFrom, policy.ApprovalReference, true, policy.PartPaymentsApproved);
        // Preparation refuses existing day evidence, so only recorded facility closures can already apply.
        var held = end.DayNumber - rateDate.DayNumber + 1;
        var closed = (await closures.GetByMonthAsync(period.Year, period.Month, ct))
            .Where(x => x.ClosureDate >= rateDate && x.ClosureDate <= end).Select(x => x.ClosureDate).Distinct().Count();
        var credit = DomainRules.DailyBilledMonthCredit(daily.Value, row.Amount, held, closed);
        return new(row.Amount, credit, NpmMonthBasis.RentGoal, rateDate, policy.ApprovalReference,
            JsonSerializer.Serialize(frozen, Json), JsonSerializer.Serialize(new { schemaVersion = 1, source = "ApprovedOperatingEvidence",
                heldDays = held, forgivenDays = closed, approvedDailyFee = daily.Value, rule = nameof(DomainRules.DailyBilledMonthCredit) }, Json));
    }

    private static NpmOpeningMonthRateEvidence? OpeningEvidence(NpmRentMonth month)
    {
        using var json = JsonDocument.Parse(month.RateEvidenceJson);
        if (!json.RootElement.TryGetProperty("assessmentRule", out var kind)) return null;
        if (kind.GetString() != FullOpeningMonthRule) throw new InvalidOperationException("Unknown prospective assessment rule.");
        var evidence = JsonSerializer.Deserialize<NpmOpeningMonthRateEvidence>(month.RateEvidenceJson, Json)
            ?? throw new InvalidOperationException("Missing prospective assessment evidence.");
        if (evidence.MunicipalityId != month.MunicipalityId || evidence.FacilityId != month.FacilityId
            || evidence.StallId != month.StallId || evidence.ContractId != month.ContractId
            || evidence.ApprovedMonthlyAmount != month.AssessedAmount || evidence.ApprovedDailyInstallment <= 0
            || evidence.MonthlyRateId == Guid.Empty || evidence.RateAsOf != month.RateAsOf
            || evidence.OccupancyStart != month.RateAsOf || evidence.ApprovalReference != month.PolicyReference
            || month.MonthBasis != NpmMonthBasis.RentGoal)
            throw new InvalidOperationException("Prospective source identity, frozen assessment and monthly authority disagree.");
        return evidence;
    }

}
