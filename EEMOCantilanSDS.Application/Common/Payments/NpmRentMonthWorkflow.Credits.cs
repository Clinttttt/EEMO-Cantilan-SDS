using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    /// <summary>Call inside the Head operating-day transaction, after saving its facts. Cash is never changed.</summary>
    public async Task SynchronizeOperatingCreditsAsync(DateOnly period, string reason, CancellationToken ct = default)
    {
        if (!await Authorized(ct) || user.Role is not ("SuperAdmin" or "Admin") || !db.HasActiveTransaction)
            throw new InvalidOperationException("Operating credits require an authorized coordinated Head transaction.");
        var sources = await db.NpmRentMonths.Where(x => x.MunicipalityId == Tenant && x.PeriodStart == period
            && x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated).OrderBy(x => x.StallId).ToListAsync(ct);
        var end = period.AddMonths(1);
        var closed = (await closures.GetByMonthAsync(period.Year, period.Month, ct)).Select(x => x.ClosureDate).ToHashSet();
        var snapshot = await rates.GetSnapshotAsync(ct);
        foreach (var source in sources)
        {
            using var prior = JsonDocument.Parse(source.CreditEvidenceJson);
            if (source.CreditedAmount > 0 && (!prior.RootElement.TryGetProperty("source", out var originalKind)
                || originalKind.GetString() != "ApprovedOperatingEvidence"))
                throw new InvalidOperationException("Existing approved credits require explicit reconciliation before operating credits can be recomputed.");
            var loaded = await Load(source.StallId, period, ct);
            var owner = EEMOCantilanSDS.Domain.Entities.Facilities.StallOccupancy.AnsweringForMonth(
                loaded.Stall.Occupancies(clock.PhilippineToday), period.Year, period.Month)!;
            var absent = (await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.StallId == source.StallId
                && x.CollectionDate >= period && x.CollectionDate < end && x.IsAbsent).Select(x => x.CollectionDate).ToListAsync(ct)).ToHashSet();
            absent.UnionWith(closed);
            var held = Enumerable.Range(0, end.DayNumber - period.DayNumber).Select(period.AddDays)
                .Where(x => x >= owner.Start && x <= owner.BillableEnd).ToArray();
            var forgiven = held.Where(absent.Contains).ToArray();
            var fee = OpeningEvidence(source)?.ApprovedDailyInstallment
                ?? NpmDailyFee.ForStallOrNull(loaded.Stall, snapshot, source.RateAsOf);
            if (fee is null || fee <= 0)
                throw new InvalidOperationException("Monthly-only absence/closure credit needs an explicit approved amount; no daily credit is invented.");
            var credit = DomainRules.DailyBilledMonthCredit(fee.Value, source.AssessedAmount, held.Length, forgiven.Length);
            var evidence = JsonSerializer.Serialize(new { schemaVersion = 1, source = "ApprovedOperatingEvidence",
                source.PeriodStart, source.ContractId, source.RateAsOf, approvedDailyFee = fee, heldDays = held.Length,
                forgivenDates = forgiven, rule = nameof(DomainRules.DailyBilledMonthCredit) }, Json);
            if (credit == source.CreditedAmount && source.CreditEvidenceJson == evidence) continue;
            db.AuditLogs.Add(source.ReviseCredits(credit, evidence, source.SettlementVersion,
                user.UserId!.Value, user.Username!, user.Role!, clock.UtcNow, reason));
        }
    }
}
