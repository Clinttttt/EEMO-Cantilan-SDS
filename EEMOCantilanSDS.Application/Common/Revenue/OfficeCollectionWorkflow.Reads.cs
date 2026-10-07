using System.Text.Json;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class OfficeCollectionWorkflow
{
    /// <summary>Canonical office-source activity only. Historical TRM trips are neither inferred nor copied.</summary>
    public async Task<Result<IReadOnlyList<SourceNativeActivityDto>>> ActivityAsync(DateOnly from, DateOnly to,
        string? operationCode = null, CancellationToken ct = default)
    {
        if (!TenantValid || !user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin" or "Collector"))
            return Result<IReadOnlyList<SourceNativeActivityDto>>.Forbidden();
        if (from > to || to.DayNumber - from.DayNumber > 366)
            return Result<IReadOnlyList<SourceNativeActivityDto>>.Failure("InvalidPeriod", ResultStatus.Invalid);
        operationCode = string.IsNullOrWhiteSpace(operationCode) ? null : operationCode;
        var collections = await db.Collections.AsNoTracking().Where(c => c.MunicipalityId == Tenant && c.BusinessDate >= from && c.BusinessDate <= to
            && (user.Role != "Collector" || c.CollectorId == user.CollectorId)
            && db.PostingOperations.Any(p => p.MunicipalityId == Tenant && p.CollectionId == c.Id && p.Origin == Origin))
            .OrderByDescending(c => c.RecordedAtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var ids = collections.Select(c => c.Id).ToArray();
        var lines = await db.CollectionLines.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.CollectionId)).ToDictionaryAsync(x => x.CollectionId, ct);
        var effects = await db.CollectionCorrections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.OriginalCollectionId))
            .GroupBy(x => x.OriginalCollectionId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.FinancialEffectAmount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var collectorIds = collections.Where(c => c.CollectorId.HasValue).Select(c => c.CollectorId!.Value).Distinct().ToArray();
        var collectors = await db.CollectorUsers.AsNoTracking().Where(c => c.MunicipalityId == Tenant && collectorIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var rows = new List<SourceNativeActivityDto>();
        foreach (var c in collections)
        {
            using var snapshot = JsonDocument.Parse(lines[c.Id].CalculationSnapshot!);
            var root = snapshot.RootElement;
            var charge = root.GetProperty("charge").Deserialize<SourceNativeChargeIntent>()!;
            if (operationCode is not null && charge.OperationCode != operationCode) continue;
            var quoteRate = root.GetProperty("Calculation");
            Guid? rateId = null; decimal? rate = null; string? vehicleName = null;
            if (quoteRate.ValueKind == JsonValueKind.Object)
            {
                if (quoteRate.TryGetProperty("RateId", out var r)) rateId = r.GetGuid();
                if (quoteRate.TryGetProperty("Rate", out var a)) rate = a.GetDecimal();
                // The name the vehicle class carried when this was recorded, frozen with the rate; never looked up again by display text.
                if (charge.VehicleClassId.HasValue && quoteRate.TryGetProperty("DisplayName", out var n) && n.ValueKind == JsonValueKind.String) vehicleName = n.GetString();
            }
            var net = c.TotalAmount + effects.GetValueOrDefault(c.Id);
            rows.Add(new(c.Id, c.ReferenceCode, c.BusinessDate, c.RecordedAtUtc, charge.OperationCode, charge.Section,
                charge.VendorRegistrationId, c.PayerName, c.CollectorId, c.CollectorId is { } collector ? collectors.GetValueOrDefault(collector) : null,
                c.TotalAmount, net, net != c.TotalAmount ? "Reversed" : "Posted", charge.CashTicketCount,
                charge.VehicleClassId, rateId, rate, charge.Kilograms, vehicleName));
        }
        return Result<IReadOnlyList<SourceNativeActivityDto>>.Success(rows);
    }
}
