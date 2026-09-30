using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;

/// <summary>
/// Derives classified cash from posted canonical evidence only. Money is the CollectionLine amount — never allocations
/// (a line's allocations restate the same money), never assessments, drafts, opening settlement, projections or provider
/// rows. A correction line counts against the classification and business-date period of the ORIGINAL line it corrects;
/// a replacement Collection is its own posted event. Nothing is written.
/// </summary>
public sealed class GetCanonicalMonthlyIncomeQueryHandler(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock clock)
    : IRequestHandler<GetCanonicalMonthlyIncomeQuery, Result<CanonicalMonthlyIncomeDto>>
{
    private const string DateBasis = "Collection business date";
    private const string SourceCoverage = "CanonicalCollectionsOnly";
    private const string CoverageNote =
        "Canonical Collection/CollectionLine money and linked corrections only. Legacy source money (PaymentRecord, "
        + "DailyCollection, UtilityBill, TPM, TRM, slaughter, online-provider rows) and opening settlement evidence are "
        + "excluded and are not combined here. This is not the official Monthly Income report and does not decide the "
        + "official cross-period RCD correction treatment.";
    private const string DisplayNameBasis =
        "Default-context policy display name effective at the period end; the semantic code when none is effective.";

    public async Task<Result<CanonicalMonthlyIncomeDto>> Handle(
        GetCanonicalMonthlyIncomeQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<CanonicalMonthlyIncomeDto>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
            return Result<CanonicalMonthlyIncomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CanonicalMonthlyIncomeDto>.Forbidden();

        var now = clock.UtcNow;
        DateTime cutoff;
        if (request.Basis == CanonicalReportingBasis.AsOf)
        {
            if (request.AsOf is not { } asOf)
                return Result<CanonicalMonthlyIncomeDto>.Failure("An AsOf report requires an explicit knowledge cutoff.", ResultStatus.Invalid);
            cutoff = asOf.UtcDateTime;
            // A future cutoff would change as events arrive and could not reproduce what was known.
            if (cutoff > now)
                return Result<CanonicalMonthlyIncomeDto>.Failure("An AsOf cutoff cannot be in the future.", ResultStatus.Invalid);
        }
        else
        {
            cutoff = now;
        }

        var periodStart = new DateOnly(request.Year, request.Month ?? 1, 1);
        var periodEnd = request.Month.HasValue
            ? periodStart.AddMonths(1).AddDays(-1)
            : new DateOnly(request.Year, 12, 31);

        var lines = await (
                from line in db.CollectionLines.AsNoTracking()
                join collection in db.Collections.AsNoTracking()
                    on new { line.MunicipalityId, Id = line.CollectionId }
                    equals new { collection.MunicipalityId, collection.Id }
                where line.MunicipalityId == tenantId
                    && collection.BusinessDate >= periodStart && collection.BusinessDate <= periodEnd
                    && collection.RecordedAtUtc <= cutoff
                select new LineFact(line.RevenueClassificationId, collection.Id, collection.BusinessDate,
                    line.Amount, line.SourceKind, line.SourcePart))
            .ToListAsync(ct);

        var corrections = await (
                from correctionLine in db.CollectionCorrectionLines.AsNoTracking()
                join correction in db.CollectionCorrections.AsNoTracking()
                    on new { correctionLine.MunicipalityId, Id = correctionLine.CorrectionId }
                    equals new { correction.MunicipalityId, correction.Id }
                join line in db.CollectionLines.AsNoTracking()
                    on new { correctionLine.MunicipalityId, Id = correctionLine.OriginalCollectionLineId }
                    equals new { line.MunicipalityId, line.Id }
                join collection in db.Collections.AsNoTracking()
                    on new { line.MunicipalityId, Id = line.CollectionId }
                    equals new { collection.MunicipalityId, collection.Id }
                where correctionLine.MunicipalityId == tenantId
                    && collection.BusinessDate >= periodStart && collection.BusinessDate <= periodEnd
                    && collection.RecordedAtUtc <= cutoff
                    && correction.RecordedAtUtc <= cutoff
                select new CorrectionFact(line.RevenueClassificationId, correction.Id, collection.BusinessDate,
                    correction.CorrectionEffectiveDate, correctionLine.FinancialEffectAmount,
                    line.SourceKind, line.SourcePart))
            .ToListAsync(ct);

        var classificationIds = lines.Select(x => x.ClassificationId)
            .Concat(corrections.Select(x => x.ClassificationId)).Distinct().ToArray();
        var classifications = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && classificationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.SemanticCode, x.IsActive })
            .ToListAsync(ct);
        var policies = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && classificationIds.Contains(x.RevenueClassificationId)
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= periodEnd)
            .Select(x => new { x.RevenueClassificationId, x.EffectiveDate, x.DisplayName })
            .ToListAsync(ct);

        int[] months = request.Month is { } single ? [single] : Enumerable.Range(1, 12).ToArray();
        CanonicalMonthlyIncomeCellDto Cell(int? month, IEnumerable<LineFact> lineFacts,
            IEnumerable<CorrectionFact> correctionFacts, Func<DateOnly, bool> insidePeriod)
        {
            var lineList = lineFacts.ToList();
            var correctionList = correctionFacts.ToList();
            var gross = lineList.Sum(x => x.Amount);
            var effect = correctionList.Sum(x => x.Effect);
            var outside = correctionList.Where(x => !insidePeriod(x.EffectiveDate)).Sum(x => x.Effect);
            return new CanonicalMonthlyIncomeCellDto(month, gross, effect, gross + effect, outside,
                lineList.Count, correctionList.Count);
        }
        bool InPeriod(DateOnly date) => date >= periodStart && date <= periodEnd;
        Func<DateOnly, bool> InMonth(int month) => date => date.Year == request.Year && date.Month == month;

        var rows = classificationIds
            .Select(id =>
            {
                var classification = classifications.SingleOrDefault(x => x.Id == id);
                var code = classification?.SemanticCode ?? "UNKNOWN_CLASSIFICATION";
                var name = policies.Where(x => x.RevenueClassificationId == id)
                    .OrderByDescending(x => x.EffectiveDate).Select(x => x.DisplayName).FirstOrDefault() ?? code;
                var classLines = lines.Where(x => x.ClassificationId == id).ToList();
                var classCorrections = corrections.Where(x => x.ClassificationId == id).ToList();
                var monthCells = months.Select(m => Cell(m,
                    classLines.Where(x => x.BusinessDate.Month == m),
                    classCorrections.Where(x => x.OriginalBusinessDate.Month == m), InMonth(m))).ToList();
                return new CanonicalMonthlyIncomeRowDto(id, code, name, classification?.IsActive ?? false,
                    monthCells, Cell(null, classLines, classCorrections, InPeriod));
            })
            .OrderBy(x => x.SemanticCode, StringComparer.Ordinal)
            .ToList();

        var monthTotals = months.Select(m => Cell(m,
            lines.Where(x => x.BusinessDate.Month == m),
            corrections.Where(x => x.OriginalBusinessDate.Month == m), InMonth(m))).ToList();
        var sources = lines.Select(x => (x.SourceKind, x.SourcePart))
            .Concat(corrections.Select(x => (x.SourceKind, x.SourcePart))).Distinct()
            .Select(key =>
            {
                var gross = lines.Where(x => x.SourceKind == key.SourceKind && x.SourcePart == key.SourcePart).Sum(x => x.Amount);
                var effect = corrections.Where(x => x.SourceKind == key.SourceKind && x.SourcePart == key.SourcePart).Sum(x => x.Effect);
                return new CanonicalMonthlyIncomeSourceDto(key.SourceKind?.ToString(), key.SourcePart?.ToString(),
                    gross, effect, gross + effect,
                    key.SourceKind is { } kind ? Domain.Constants.CollectionSourceAuthorityMap.For(kind).ToString() : null);
            })
            .OrderBy(x => x.SourceKind, StringComparer.Ordinal).ThenBy(x => x.SourcePart, StringComparer.Ordinal)
            .ToList();

        return Result<CanonicalMonthlyIncomeDto>.Success(new CanonicalMonthlyIncomeDto(
            tenantId, request.Year, request.Month, periodStart, periodEnd, request.Basis, cutoff,
            DateBasis, SourceCoverage, false, CoverageNote, DisplayNameBasis, rows, monthTotals,
            Cell(null, lines, corrections, InPeriod), sources,
            lines.Select(x => x.CollectionId).Distinct().Count(),
            corrections.Select(x => x.CorrectionId).Distinct().Count()));
    }

    private sealed record LineFact(Guid ClassificationId, Guid CollectionId, DateOnly BusinessDate, decimal Amount,
        CollectionSourceKind? SourceKind, CollectionSourcePart? SourcePart);

    private sealed record CorrectionFact(Guid ClassificationId, Guid CorrectionId, DateOnly OriginalBusinessDate,
        DateOnly EffectiveDate, decimal Effect, CollectionSourceKind? SourceKind, CollectionSourcePart? SourcePart);
}
