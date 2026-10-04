using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;

/// <summary>
/// Office Collection Activity: the authoritative legacy and canonical collection events of one tenant, each real collection
/// exactly once (<see cref="ICollectionActivityReader"/>). Head/Admin only; the tenant is the resolved request tenant and a
/// caller whose claim names another tenant is refused. Nothing is written.
/// </summary>
public sealed class GetCollectionActivityQueryHandler(
    ICollectionActivityReader reader,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock clock)
    : IRequestHandler<GetCollectionActivityQuery, Result<CollectionActivityFeedDto>>
{
    public const string DateBasis =
        "Canonical: Collection business date. Legacy: the Philippine date the source recorded the payment "
        + "(paid/updated moment; market and slaughter day for Tabo and slaughterhouse).";
    public const string CorrectionBasis = "LatestCorrected";

    private static readonly IReadOnlyList<string> Notes =
    [
        "Each real collection is listed once: a legacy source row while its legacy money is authoritative, the posted "
        + "Collection after the row's cutover. A converted row's legacy fields are never listed beside its Collection.",
        "A correction stays under the event it corrects (LatestCorrected: every correction recorded so far); a replacement "
        + "Collection is its own event. A document correction moves no money.",
        "Drafts, assessments, opening settlement, remittances, shadow rows and online-provider transactions are not collections "
        + "and are not listed.",
        "Canonical Collections are identified by their StallTrack Reference Code (SRC). Legacy rows carry the document number the source recorded and no SRC; their instrument is not inferred."
    ];

    public async Task<Result<CollectionActivityFeedDto>> Handle(GetCollectionActivityQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<CollectionActivityFeedDto>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
            return Result<CollectionActivityFeedDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CollectionActivityFeedDto>.Forbidden();

        if (!string.IsNullOrWhiteSpace(request.Reference))
        {
            var code = request.Reference.Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^SRC-[0-9]{4}-[0-9]{6,}$"))
                return Result<CollectionActivityFeedDto>.Failure("Enter a full reference such as SRC-2026-000127.", ResultStatus.Invalid);
            var found = await reader.FindBusinessDateByReferenceAsync(tenantId, code, ct);
            var day = found ?? request.From;
            var matched = found is null ? new List<CollectionActivityEventDto>()
                : (await reader.GetAsync(tenantId, day, day, ct)).Where(e => e.Authority == "Canonical" && e.ReferenceCode == code).ToList();
            return Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(
                day, day, DateBasis, CorrectionBasis, clock.UtcNow, matched, matched.Count, false,
                0m, matched.Sum(e => e.Amount), matched.Sum(e => e.CorrectionEffect),
                matched.Sum(e => e.Amount + e.CorrectionEffect), Notes));
        }
        if (request.From > request.To || request.To.DayNumber - request.From.DayNumber >= GetCollectionActivityQuery.MaxDays)
            return Result<CollectionActivityFeedDto>.Failure(
                $"Choose a period of 1 to {GetCollectionActivityQuery.MaxDays} days.", ResultStatus.Invalid);
        if (request.Limit is < 1 or > GetCollectionActivityQuery.MaxLimit)
            return Result<CollectionActivityFeedDto>.Failure(
                $"Limit must be between 1 and {GetCollectionActivityQuery.MaxLimit}.", ResultStatus.Invalid);
        if (request.Authority is not (null or "Legacy" or "Canonical"))
            return Result<CollectionActivityFeedDto>.Failure("Authority must be Legacy or Canonical.", ResultStatus.Invalid);

        var events = (await reader.GetAsync(tenantId, request.From, request.To, ct))
            .Where(e => request.Facility is null || e.Facility == request.Facility)
            .Where(e => request.CollectorId is null || e.CollectorId == request.CollectorId)
            .Where(e => request.Authority is null || e.Authority == request.Authority)
            .OrderByDescending(e => e.BusinessDate)
            .ThenByDescending(e => e.RecordedAtUtc)
            .ThenBy(e => e.EventKey, StringComparer.Ordinal)
            .ToList();

        var legacy = events.Where(e => e.Authority == "Legacy").Sum(e => e.Amount);
        var canonical = events.Where(e => e.Authority == "Canonical").Sum(e => e.Amount);
        var effect = events.Sum(e => e.CorrectionEffect);
        return Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(
            request.From, request.To, DateBasis, CorrectionBasis, clock.UtcNow,
            events.Take(request.Limit).ToList(), events.Count, events.Count > request.Limit,
            legacy, canonical, effect, legacy + canonical + effect, Notes));
    }
}
