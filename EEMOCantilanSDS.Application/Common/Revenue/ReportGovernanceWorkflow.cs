using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Head-authorized report metadata only. Operational financial ledgers are never mutated here.</summary>
public sealed class ReportGovernanceWorkflow(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant, IClock clock, ILegacyMonthlyIncomeReader legacy)
{
    public Task<Result<ReportRevisionDto>> SetTargetAsync(SetAnnualTargetRequest request, CancellationToken ct = default)
    {
        if (!Authorized(true)) return Task.FromResult(Result<ReportRevisionDto>.Forbidden());
        if (request.Amount < 0m || !ValidReportMoney(request.Amount))
            return Task.FromResult(Result<ReportRevisionDto>.Failure("InvalidReportAmount", ResultStatus.Invalid));
        return AppendAsync(request.ClientOperationId, OfficialReportRevisionKind.AnnualTarget, request.RowKey, request.Year, 0,
            request.Amount, null, request.ApprovedSource, request.Reference, request.Note, request.ExpectedRevisionId,
            ct);
    }

    public Task<Result<ReportRevisionDto>> AdjustAsync(SetMonthlyIncomeAdjustmentRequest request, CancellationToken ct = default)
    {
        if (!Authorized(true)) return Task.FromResult(Result<ReportRevisionDto>.Forbidden());
        if (!ValidReportMoney(request.ExpectedSystemAmount) || !ValidReportMoney(request.OfficialAmount))
            return Task.FromResult(Result<ReportRevisionDto>.Failure("InvalidReportAmount", ResultStatus.Invalid));
        return AppendAsync(request.ClientOperationId, OfficialReportRevisionKind.MonthlyAdjustment, request.RowKey, request.Year,
            request.Month, request.OfficialAmount - request.ExpectedSystemAmount, request.ExpectedSystemAmount,
            request.Reason, request.Reference, null, request.ExpectedRevisionId, ct);
    }

    private static bool ValidReportMoney(decimal amount) => decimal.Round(amount, 2) == amount &&
        amount is >= -9999999999999999.99m and <= 9999999999999999.99m; // storage precision, not a fee ceiling

    public async Task<Result<IReadOnlyList<ReportRevisionDto>>> HistoryAsync(int year, CancellationToken ct = default)
    {
        if (!Authorized(false)) return Result<IReadOnlyList<ReportRevisionDto>>.Forbidden();
        if (year is < 2000 or > 2200) return Result<IReadOnlyList<ReportRevisionDto>>.Failure("Choose a valid year.", ResultStatus.Invalid);
        var rows = await db.OfficialReportRevisions.AsNoTracking().Where(x => x.MunicipalityId == tenant.MunicipalityId && x.Year == year)
            .OrderBy(x => x.RowKey).ThenBy(x => x.Kind).ThenBy(x => x.Month).ThenByDescending(x => x.Revision).ToListAsync(ct);
        return Result<IReadOnlyList<ReportRevisionDto>>.Success(rows.Select(ToDto).ToArray());
    }

    private bool Authorized(bool write) => user.IsAuthenticated && user.UserId is { } id && id != Guid.Empty &&
        (write ? user.Role == "SuperAdmin" : user.Role is "SuperAdmin" or "Admin") && tenant.MunicipalityId != Guid.Empty &&
        (user.MunicipalityId is null || user.MunicipalityId == tenant.MunicipalityId);

    private async Task<Result<ReportRevisionDto>> AppendAsync(Guid operationId, OfficialReportRevisionKind kind, string key,
        int year, int month, decimal amount, decimal? system, string reason, string? reference, string? note,
        Guid? expectedRevision, CancellationToken ct)
    {
        if (!Authorized(true)) return Result<ReportRevisionDto>.Forbidden();
        if (operationId == Guid.Empty || !OfficialMonthlyIncomeStructure.Rows.Any(x => x.Key == key))
            return Result<ReportRevisionDto>.Failure("Choose an official revenue row and a stable operation identity.", ResultStatus.Invalid);
        var intent = JsonSerializer.Serialize(new { Actor = user.UserId, kind, key, year, month,
            Amount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            SystemAmount = system?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Reason = reason?.Trim(), Reference = reference?.Trim(), Note = note?.Trim(), expectedRevision });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(intent)));
        try
        {
            await using var transaction = await db.BeginSerializableTransactionAsync(ct);
            var prior = await db.OfficialReportRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenant.MunicipalityId && x.ClientOperationId == operationId, ct);
            if (prior is not null)
                return prior.IntentFingerprint == fingerprint ? Result<ReportRevisionDto>.Success(ToDto(prior))
                    : Result<ReportRevisionDto>.Failure("ReportIntentConflict", ResultStatus.Conflict);
            var last = await db.OfficialReportRevisions.AsNoTracking().Where(x => x.MunicipalityId == tenant.MunicipalityId &&
                x.Kind == kind && x.RowKey == key && x.Year == year && x.Month == month).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
            if (last?.Id != expectedRevision) return Result<ReportRevisionDto>.Failure("ReportRevisionChanged", ResultStatus.Conflict);
            if (kind == OfficialReportRevisionKind.MonthlyAdjustment)
            {
                var report = await new GetOfficialMonthlyIncomeQueryHandler(db, legacy, user, tenant, clock).Handle(new(year, month), ct);
                if (!report.IsSuccess) return Result<ReportRevisionDto>.Failure(report.Error!, report.Status);
                var row = report.Value!.Groups.SelectMany(g => g.Rows).SingleOrDefault(x => x.Key == key);
                var actual = row?.Months[month - 1].SystemAmount ?? 0m;
                if (actual != system) return Result<ReportRevisionDto>.Failure("SystemAmountChanged", ResultStatus.Conflict);
            }
            var revision = OfficialReportRevision.Record(tenant.MunicipalityId, operationId, fingerprint, kind, key, year,
                month, (last?.Revision ?? 0) + 1, last?.Id, amount, system, reason, reference, note,
                user.UserId!.Value, user.Username ?? "Head", clock.UtcNow);
            db.OfficialReportRevisions.Add(revision);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<ReportRevisionDto>.Success(ToDto(revision));
        }
        catch (ArgumentException ex) { return Result<ReportRevisionDto>.Failure(ex.Message, ResultStatus.Invalid); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return Result<ReportRevisionDto>.Failure("ReportRevisionChanged", ResultStatus.Conflict); }
        catch (System.Data.Common.DbException ex) when (ex.GetType().GetProperty("SqlState")?.GetValue(ex) is "40001" or "40P01")
        { db.ChangeTracker.Clear(); return Result<ReportRevisionDto>.Failure("ReportRevisionChanged", ResultStatus.Conflict); }
    }

    public static ReportRevisionDto ToDto(OfficialReportRevision x) => new(x.Id, x.Kind, x.RowKey, x.Year, x.Month,
        x.Revision, x.SupersedesId, x.Amount, x.SystemAmountAtRevision, x.SourceOrReason, x.Reference, x.Note,
        x.ActorId, x.ActorName, x.RecordedAtUtc);
}
