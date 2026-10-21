using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    /// <summary>Dormant Head preparation. Does not reconcile history, activate a source or create cash.</summary>
    public async Task<Result<Guid>> PrepareAsync(Guid stallId, Guid contractId, DateOnly period,
        NpmRentMonthAssessmentEvidence evidence, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<Guid>.Forbidden();
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        try
        {
            if (await db.NpmRentMonths.AnyAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId && x.PeriodStart == period, ct))
                return Result<Guid>.Failure("A month already exists for this tenant/stall/period. Review that source; do not create another obligation.", ResultStatus.Conflict);
            var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
            var contract = stall?.Contracts.SingleOrDefault(x => x.Id == contractId && x.MunicipalityId == Tenant);
            if (stall?.Facility is null || contract is null) return Result<Guid>.NotFound();
            var prepared = NpmRentMonth.Prepare(stall.Facility, stall, contract, period, clock.PhilippineToday,
                evidence, user.UserId!.Value, user.Username!, user.Role!, clock.UtcNow);
            db.NpmRentMonths.Add(prepared.Month); db.AuditLogs.Add(prepared.Audit);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Result<Guid>.Success(prepared.Month.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<Guid>.Failure("Month preparation was not confirmed. Refresh the source identity and reviewed evidence before retrying.", ResultStatus.Conflict);
        }
    }

    /// <summary>Activates reviewed financial authority using the authenticated Head, preapproved tenant policy and unchanged opening evidence. Also called inside eligible Head registration.</summary>
    public async Task<Result<long>> ReviewAndActivateAsync(Guid stallId, DateOnly period, long expectedVersion,
        NpmRentMonthOpeningEvidence opening, string decisionReference, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin")) return Result<long>.Forbidden();
        if (assessments is null || string.IsNullOrWhiteSpace(decisionReference))
            return Result<long>.Failure("Approved assessment authority and an explicit activation decision are required.", ResultStatus.Conflict);
        await using var transaction = db.HasActiveTransaction ? null : await db.BeginSerializableTransactionAsync(ct);
        try
        {
            var source = await db.NpmRentMonths.SingleAsync(x => x.MunicipalityId == Tenant && x.StallId == stallId && x.PeriodStart == period, ct);
            var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts).SingleAsync(x => x.MunicipalityId == Tenant && x.Id == stallId, ct);
            if (OpeningEvidence(source) is { } openingAuthority)
            {
                if (user.Role != "SuperAdmin") return Result<long>.Forbidden();
                var contract = stall.Contracts.Single(x => x.MunicipalityId == Tenant && x.Id == source.ContractId);
                var authority = await ResolveOpeningAssessmentAsync(stall, contract, period, ct);
                if (openingAuthority != System.Text.Json.JsonSerializer.Deserialize<NpmOpeningMonthRateEvidence>(authority.RateEvidenceJson, Json)
                    || source.CreditedAmount != authority.CreditedAmount)
                    return Result<long>.Failure("Prospective approval, rate or credit evidence changed; review a separate assessment revision before activation.", ResultStatus.Conflict);
            }
            else
            {
                var authority = await assessments.ResolveAssessmentAsync(stall, period.Year, period.Month, true, ct);
                if (source.MonthBasis != (await rates.GetSnapshotAsync(ct)).MonthRule.Basis
                    || source.AssessedAmount != authority.Obligation || source.CreditedAmount != authority.Credits)
                    return Result<long>.Failure("Prepared assessment/credit evidence differs from canonical approved rules. Reconcile it explicitly before activation.", ResultStatus.Conflict);
            }
            var json = System.Text.Json.JsonSerializer.Serialize(opening, Json);
            ParseOpening(json);
            db.AuditLogs.Add(source.Reconcile(expectedVersion, user.UserId!.Value, user.Username!, user.Role!, clock.UtcNow, json));
            db.AuditLogs.Add(source.ActivateSettlement(source.SettlementVersion, user.UserId.Value, user.Username!, user.Role!, clock.UtcNow, decisionReference));
            // Validate complete original allocations, legacy fingerprint and responsible contract before persisting activation.
            await Load(stallId, period, ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Result<long>.Success(source.SettlementVersion);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Text.Json.JsonException
            or DbUpdateException or System.Data.Common.DbException)
        {
            db.ChangeTracker.Clear();
            return Result<long>.Failure("Activation was not confirmed. Reviewed ownership, policy and opening evidence must agree.", ResultStatus.Conflict);
        }
    }
}
