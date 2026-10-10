using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Assessment-only workflow. Never calls Composer, creates rental periods or posts cash.</summary>
public sealed class FineAssessmentWorkflow(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor municipality, IClock clock)
{
    private Guid Tenant => municipality.MunicipalityId;
    private Result<T>? Guard<T>(bool head = false)
    {
        if (!user.IsAuthenticated || user.UserId is null || user.UserId == Guid.Empty) return Result<T>.Unauthorized();
        if (Tenant == Guid.Empty || user.MunicipalityId != Tenant || user.Role is not ("Admin" or "SuperAdmin") || head && user.Role != "SuperAdmin")
            return Result<T>.Forbidden();
        return null;
    }
    private static FinePolicyDto PolicyDto(FineAssessmentPolicy p) => new(p.Id, new(p.PenaltyVersionId, p.AccountKind,
        p.Facility, p.GoverningReference, p.DateBasis, p.AllowsUncappedAmount, p.AllowsClosedAccount));

    public async Task<Result<IReadOnlyList<FinePolicyDto>>> PoliciesAsync(CancellationToken ct = default)
    {
        if (Guard<IReadOnlyList<FinePolicyDto>>() is { } denied) return denied;
        var policies = await db.FineAssessmentPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant)
            .OrderByDescending(x => x.RecordedAtUtc).ToListAsync(ct);
        var ids = policies.Select(p => p.PenaltyVersionId).ToArray();
        var definitions = await db.PenaltyDefinitions.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return Result<IReadOnlyList<FinePolicyDto>>.Success(policies.Select(p => {
            var d = definitions[p.PenaltyVersionId];
            return PolicyDto(p) with { Definition = new(d.Id,d.Code,d.DisplayName,d.AppliesTo,d.Basis,d.FixedAmount,d.MaximumAmount,d.IsActive,d.EffectiveDate) };
        }).ToList());
    }
    public async Task<Result<FinePolicyDto>> RecordPolicyAsync(FinePolicyRequest r, CancellationToken ct = default)
    {
        if (Guard<FinePolicyDto>(true) is { } denied) return denied;
        var version = await db.PenaltyDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == r.PenaltyVersionId, ct);
        if (version is null) return Result<FinePolicyDto>.NotFound();
        if (!version.IsActive || version.EffectiveDate > clock.PhilippineToday)
            return Result<FinePolicyDto>.Failure("Record evidence only for an active, effective approved definition.", ResultStatus.Invalid);
        if (r.AccountKind == FineAccountKind.FacilityContract && r.Facility is not (FacilityCode.NPM or FacilityCode.NCC or FacilityCode.TCC or FacilityCode.BBQ or FacilityCode.ICE))
            return Result<FinePolicyDto>.Failure("This rental source has no verified assessment adapter.", ResultStatus.Invalid);
        try
        {
            var policy = FineAssessmentPolicy.Create(Tenant, version.Id, r.AccountKind, r.Facility, r.GoverningReference,
                r.DateBasis, r.AllowsUncappedAmount, r.AllowsClosedAccount, user.UserId!.Value, clock.UtcNow);
            db.FineAssessmentPolicies.Add(policy); await db.SaveChangesAsync(ct);
            return Result<FinePolicyDto>.Success(PolicyDto(policy));
        }
        catch (ArgumentException ex) { return Result<FinePolicyDto>.Failure(ex.Message, ResultStatus.Invalid); }
    }

    private sealed record AccountFacts(FineAccountOption Option, FacilityCode? Facility, DateOnly Start, DateOnly End);
    private static ObligationKind? ObligationKindOf(FineAccountKind k) => k switch {
        FineAccountKind.Kanmanggay => ObligationKind.KanmanggaySpaceRental,
        FineAccountKind.VegetableFruitMonthly => ObligationKind.VegetableFruitSpaceRental,
        FineAccountKind.FiestaAraw => ObligationKind.FiestaArawLotRental, _ => null };
    private async Task<AccountFacts?> ResolveAsync(FineAccountRef account, CancellationToken ct)
    {
        if (account.AccountId == Guid.Empty) return null;
        if (account.Kind == FineAccountKind.FacilityContract)
        {
            var anchor = await db.Contracts.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == account.AccountId, ct);
            if (anchor is null) return null;
            var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == anchor.StallId, ct);
            var contract = stall?.Contracts.SingleOrDefault(x => x.Id == account.AccountId && x.MunicipalityId == Tenant);
            if (contract is null || stall?.Facility is null || stall.MunicipalityId != Tenant ||
                stall.Facility.Code is not (FacilityCode.NPM or FacilityCode.NCC or FacilityCode.TCC or FacilityCode.BBQ or FacilityCode.ICE)) return null;
            var occupancy = stall.Occupancies(clock.PhilippineToday).SingleOrDefault(x => x.Contract.Id == contract.Id);
            if (occupancy is null) return null;
            return new(new(account, contract.ActualOccupant, stall.StallNo, stall.Facility.Name, !occupancy.IsCurrent, stall.Facility.Code),
                stall.Facility.Code, occupancy.Start, occupancy.End);
        }
        var kind = ObligationKindOf(account.Kind);
        if (kind is null) return null;
        var a = await db.ObligationAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant &&
            x.Id == account.AccountId && x.Kind == kind, ct);
        if (a is null || string.IsNullOrWhiteSpace(a.ActualOccupant)) return null;
        return new(new(account, a.ActualOccupant, a.SubjectLabel, ObligationCollectionSource.KindLabel(a.Kind),
            !a.IsActiveOn(clock.PhilippineToday)), null, a.ActiveFrom, a.ActiveTo ?? DateOnly.MaxValue);
    }
    public async Task<Result<IReadOnlyList<FineAccountOption>>> SearchAccountsAsync(string? query, CancellationToken ct = default)
    {
        if (Guard<IReadOnlyList<FineAccountOption>>() is { } denied) return denied;
        var text = (query ?? "").Trim();
        if (text.Length is < 2 or > 100) return Result<IReadOnlyList<FineAccountOption>>.Success([]);
        var policies = await db.FineAssessmentPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant).ToListAsync(ct);
        var result = new List<FineAccountOption>();
        var contractIds = await db.Contracts.AsNoTracking().Where(x => x.MunicipalityId == Tenant &&
            (x.ActualOccupant.Contains(text) || x.Stall!.StallNo.Contains(text))).OrderBy(x => x.ActualOccupant).ThenBy(x => x.Id)
            .Select(x => x.Id).Take(40).ToListAsync(ct);
        var obligationIds = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == Tenant &&
            (x.ActualOccupant!.Contains(text) || x.SubjectLabel.Contains(text))).OrderBy(x => x.ActualOccupant).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Kind }).Take(40).ToListAsync(ct);
        var refs = contractIds.Select(id => new FineAccountRef(FineAccountKind.FacilityContract, id)).Concat(obligationIds.Select(a =>
            new FineAccountRef(a.Kind switch { ObligationKind.KanmanggaySpaceRental => FineAccountKind.Kanmanggay,
                ObligationKind.VegetableFruitSpaceRental => FineAccountKind.VegetableFruitMonthly,
                ObligationKind.FiestaArawLotRental => FineAccountKind.FiestaAraw, _ => (FineAccountKind)0 }, a.Id)));
        foreach (var account in refs)
        {
            var facts = await ResolveAsync(account, ct);
            if (facts is not null && policies.Any(p => p.AccountKind == account.Kind && p.Facility == facts.Facility &&
                (!facts.Option.Closed || p.AllowsClosedAccount))) result.Add(facts.Option);
        }
        return Result<IReadOnlyList<FineAccountOption>>.Success(result);
    }

    public async Task<Result<FineAssessmentDto>> CreateAsync(CreateFineAssessmentRequest r, CancellationToken ct = default)
    {
        if (Guard<FineAssessmentDto>(true) is { } denied) return denied;
        if (r.Account is null || r.ClientOperationId == Guid.Empty || r.ViolationDate > clock.PhilippineToday)
            return Result<FineAssessmentDto>.Failure("Select an account, valid incident date and operation identity.", ResultStatus.Invalid);
        // Validate precision before cent-normalizing the fingerprint: an invalid sub-cent intent must not replay a valid amount.
        if (r.Amount <= 0m || r.Amount > Collection.MaximumMoneyAmount || decimal.Round(r.Amount, 2, MidpointRounding.ToZero) != r.Amount)
            return Result<FineAssessmentDto>.Failure("Enter a positive amount with centavo precision.", ResultStatus.Invalid);
        var incident = string.IsNullOrWhiteSpace(r.IncidentReference) ? null : r.IncidentReference.Trim().ToUpperInvariant();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Tenant, Actor = user.UserId, r.Account, r.PolicyId, r.ViolationDate, Incident = incident,
            Description = r.Description?.Trim(), Amount = r.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) }))));
        var prior = await ReplayAsync(r.ClientOperationId, fingerprint, ct);
        if (prior is not null) return prior;
        var facts = await ResolveAsync(r.Account, ct);
        if (facts is null) return Result<FineAssessmentDto>.NotFound();
        if (r.ViolationDate < facts.Start || r.ViolationDate > facts.End)
            return Result<FineAssessmentDto>.Failure("The violation date does not belong to this liable registration. Select the historical occupant's actual account.", ResultStatus.Invalid);
        var policy = await db.FineAssessmentPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == r.PolicyId, ct);
        if (policy is null || policy.AccountKind != r.Account.Kind || policy.Facility != facts.Facility || facts.Option.Closed && !policy.AllowsClosedAccount)
            return Result<FineAssessmentDto>.Failure("No governing policy authorizes this penalty/source combination.", ResultStatus.Invalid);
        var penalty = await db.PenaltyDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == policy.PenaltyVersionId, ct);
        if (penalty is null) return Result<FineAssessmentDto>.NotFound();
        var versionDate = policy.DateBasis == FinePenaltyDateBasis.ViolationDate ? r.ViolationDate : clock.PhilippineToday;
        var versions = await db.PenaltyDefinitions.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Code == penalty.Code).ToListAsync(ct);
        if (PenaltyDefinition.Resolve(versions, penalty.Code, versionDate)?.Id != penalty.Id || !penalty.IsActive ||
            PenaltyDefinition.Resolve(versions, penalty.Code, clock.PhilippineToday)?.IsActive != true)
            return Result<FineAssessmentDto>.Failure("The approved penalty version is not eligible for this incident/date. Reload the governing policy.", ResultStatus.Invalid);
        var possible = await db.FineAssessments.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant &&
            x.AccountKind == r.Account.Kind && (x.ContractId == r.Account.AccountId || x.ObligationAccountId == r.Account.AccountId) &&
            x.PenaltyCode == penalty.Code && (incident != null ? x.IncidentReference == incident :
                x.ViolationDate == r.ViolationDate && x.Description == (r.Description ?? "").Trim()), ct);
        if (possible && (incident != null || !r.ConfirmPossibleDuplicate))
            return Result<FineAssessmentDto>.Failure(incident != null ? "This incident reference is already assessed for this account and penalty." :
                "POSSIBLE_DUPLICATE: A similar incident is already assessed. Review it before confirming a distinct incident.", ResultStatus.Conflict);
        try
        {
            var evidence = JsonSerializer.Serialize(new { policy.Id, policy.GoverningReference, policy.DateBasis,
                policy.AllowsUncappedAmount, policy.AllowsClosedAccount, penalty.Basis, penalty.FixedAmount, penalty.MaximumAmount,
                penalty.EffectiveDate, VersionDate = versionDate, LiableAccount = r.Account, OccupancyStart = facts.Start,
                OccupancyEnd = facts.End, Role = user.Role, Actor = user.UserId });
            var a = FineAssessment.Create(Tenant, r.Account.Kind, r.Account.AccountId, policy, penalty,
                facts.Option.Occupant, facts.Option.Unit, facts.Option.Source, r.ViolationDate, clock.PhilippineToday,
                incident, r.Description ?? "", r.Amount, r.ClientOperationId, fingerprint, user.UserId!.Value, clock.UtcNow, evidence);
            db.FineAssessments.Add(a); db.FineAssessmentEvents.Add(FineAssessmentEvent.Created(a));
            await db.SaveChangesAsync(ct);
            return Result<FineAssessmentDto>.Success(ToDto(a, policy.GoverningReference, incident is null ? "No case reference: real-world duplicate prevention is limited." : null));
        }
        catch (ArgumentException ex) { return Result<FineAssessmentDto>.Failure(ex.Message, ResultStatus.Invalid); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await ReplayAsync(r.ClientOperationId, fingerprint, ct) ?? Result<FineAssessmentDto>.Failure(
                "The incident was saved concurrently or conflicts with another assessment. Reload before retrying.", ResultStatus.Conflict);
        }
    }
    private async Task<Result<FineAssessmentDto>?> ReplayAsync(Guid operation, string fingerprint, CancellationToken ct)
    {
        var prior = await db.FineAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == operation, ct);
        if (prior is null) return null;
        if (prior.RequestFingerprint != fingerprint) return Result<FineAssessmentDto>.Failure("IDEMPOTENCY_CONFLICT: The operation identity is already bound to different assessment details.", ResultStatus.Conflict);
        var policy = await db.FineAssessmentPolicies.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == prior.PolicyId, ct);
        var settled = (await new FineAssessmentCollectionSource(db).SettledAsync(Tenant, [prior.Id], ct)).GetValueOrDefault(prior.Id);
        return Result<FineAssessmentDto>.Success(ToDto(prior, policy.GoverningReference, settled: settled));
    }
    private static FineAssessmentDto ToDto(FineAssessment a, string reference, string? warning = null, decimal settled = 0m) => new(a.Id,
        new(a.AccountKind, a.ContractId ?? a.ObligationAccountId!.Value), a.Occupant, a.UnitReference, a.SourceName,
        a.PenaltyName, a.Description, a.ViolationDate, a.BusinessDate, a.IncidentReference, a.AssessedAmount, settled,
        a.AssessedAmount - settled, settled == a.AssessedAmount ? "Settled" : settled > 0 ? "Partial" : "Outstanding", a.Version, reference, a.RecordedAtUtc, warning);
    public async Task<Result<IReadOnlyList<FineAssessmentDto>>> ReadAsync(FineAccountKind? kind = null, Guid? accountId = null,
        string? search = null, CancellationToken ct = default)
    {
        if (Guard<IReadOnlyList<FineAssessmentDto>>() is { } denied) return denied;
        if (kind.HasValue != accountId.HasValue) return Result<IReadOnlyList<FineAssessmentDto>>.Failure("Account kind and identity must be supplied together.", ResultStatus.Invalid);
        var query = db.FineAssessments.AsNoTracking().Where(x => x.MunicipalityId == Tenant);
        if (kind is not null) query = query.Where(x => x.AccountKind == kind && (x.ContractId == accountId || x.ObligationAccountId == accountId));
        if (!string.IsNullOrWhiteSpace(search)) { var text = search.Trim(); query = query.Where(x => x.Occupant.Contains(text) || x.UnitReference.Contains(text) || x.Description.Contains(text)); }
        var rows = await (from a in query join p in db.FineAssessmentPolicies.AsNoTracking() on new { a.MunicipalityId, Id = a.PolicyId }
            equals new { p.MunicipalityId, p.Id } orderby a.BusinessDate descending, a.Id select new { Assessment = a, p.GoverningReference }).Take(201).ToListAsync(ct);
        if (rows.Count > 200) return Result<IReadOnlyList<FineAssessmentDto>>.Failure("More than 200 assessments match. Narrow the search; totals have not been truncated.", ResultStatus.Invalid);
        var settled = await new FineAssessmentCollectionSource(db).SettledAsync(Tenant, rows.Select(x => x.Assessment.Id).ToArray(), ct);
        if (rows.Any(x => settled.GetValueOrDefault(x.Assessment.Id) < 0m || settled.GetValueOrDefault(x.Assessment.Id) > x.Assessment.AssessedAmount))
            return Result<IReadOnlyList<FineAssessmentDto>>.Failure("Fine settlement evidence does not reconcile.", ResultStatus.Conflict);
        return Result<IReadOnlyList<FineAssessmentDto>>.Success(rows.Select(x => ToDto(x.Assessment, x.GoverningReference,
            settled: settled.GetValueOrDefault(x.Assessment.Id))).ToList());
    }
    public async Task<Result<FineAccountOption>> AccountAsync(FineAccountRef account, CancellationToken ct = default)
    {
        if (Guard<FineAccountOption>() is { } denied) return denied;
        var facts = await ResolveAsync(account, ct);
        return facts is null ? Result<FineAccountOption>.NotFound() : Result<FineAccountOption>.Success(facts.Option);
    }
    public async Task<Result<IReadOnlyList<FineAssessmentEventDto>>> HistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (Guard<IReadOnlyList<FineAssessmentEventDto>>() is { } denied) return denied;
        if (!await db.FineAssessments.AnyAsync(x => x.MunicipalityId == Tenant && x.Id == id, ct)) return Result<IReadOnlyList<FineAssessmentEventDto>>.NotFound();
        return Result<IReadOnlyList<FineAssessmentEventDto>>.Success(await db.FineAssessmentEvents.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && x.AssessmentId == id).OrderBy(x => x.RecordedAtUtc)
            .Select(x => new FineAssessmentEventDto(x.Id, x.EventKind, x.PreviousAmount, x.ResultingAmount, x.ActorId, x.RecordedAtUtc, x.Evidence)).ToListAsync(ct));
    }
}
