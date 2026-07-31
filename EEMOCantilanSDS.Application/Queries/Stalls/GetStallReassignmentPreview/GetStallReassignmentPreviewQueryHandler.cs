using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallReassignmentPreview;

/// <summary>
/// Reads the stall a payor used to hold and works out what a stall of their own would look like: the same facility
/// and section, the rate and term they were on, and the next free number in that section.
///
/// Everything is derived from stored data — the facility's own billing archetype decides whether the fee is daily,
/// and the section label is the tenant's own wording — so a municipality that names its sections differently, or
/// does not use sections at all, is described in its own terms.
/// </summary>
public class GetStallReassignmentPreviewQueryHandler(
    IStallRepository stallRepo,
    IFacilityRepository facilityRepo) : IRequestHandler<GetStallReassignmentPreviewQuery, Result<StallReassignmentPreviewDto>>
{
    public async Task<Result<StallReassignmentPreviewDto>> Handle(
        GetStallReassignmentPreviewQuery request, CancellationToken ct)
    {
        var stall = await stallRepo.GetByIdWithContractsAsync(request.PreviousStallId, ct);
        if (stall is null)
            return Result<StallReassignmentPreviewDto>.NotFound();

        var facilityCode = await stallRepo.GetFacilityCodeByStallIdAsync(request.PreviousStallId, ct);
        if (facilityCode is null)
            return Result<StallReassignmentPreviewDto>.NotFound();

        var facility = await facilityRepo.GetByCodeAsync(facilityCode.Value, ct);
        if (facility is null)
            return Result<StallReassignmentPreviewDto>.NotFound();

        // Whose placement this is. The term must be named, because a re-let stall holds several: reading "the
        // latest contract" would pick up the SITTING lessee and place them in a second stall. The register passes
        // the id of the term each of its rows is the record of; the fallback covers a stall with a single history.
        var lastContract = PastOccupancyContract.Resolve(stall, request.ContractId);

        if (lastContract is null)
            return Result<StallReassignmentPreviewDto>.Failure("That stall has no contract to read the lessee from.", 409);

        var sectionLabel = stall.Section is { } section
            ? facility.SectionLabel(section) ?? section.ToString()
            : stall.CustomSectionName ?? string.Empty;

        return Result<StallReassignmentPreviewDto>.Success(new StallReassignmentPreviewDto(
            stall.Id,
            facility.Name,
            stall.StallNo,
            sectionLabel,
            lastContract.ActualOccupant,
            lastContract.NameOnContract,
            lastContract.MonthlyRentalRate > 0 ? lastContract.MonthlyRentalRate : stall.MonthlyRate,
            facility.Archetype == BillingArchetype.DailyStall,
            await SuggestStallNoAsync(facilityCode.Value, stall, ct),
            // Kept inside what a contract may run for, so a legacy record with a longer term does not pre-fill the
            // form with a figure the create path would refuse.
            Math.Clamp(lastContract.DurationYears, 1, 10)));
    }

    /// <summary>
    /// One past the highest number in that facility and section. Purely a suggestion for the form: the create path
    /// re-checks uniqueness, so two clerks preparing a stall at the same time cannot both register the same number
    /// — the second is told it is taken. Numbers that are not plain integers are ignored when finding the highest.
    /// </summary>
    private async Task<string> SuggestStallNoAsync(
        FacilityCode facilityCode, Domain.Entities.Facilities.Stall stall, CancellationToken ct)
    {
        var siblings = await stallRepo.GetStallsWithContractsByFacilityAsync(
            facilityCode, stall.Section, stall.CustomSectionName, ct);

        var highest = siblings
            .Select(s => int.TryParse(s.StallNo.Trim(), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return highest > 0 ? (highest + 1).ToString() : string.Empty;
    }
}
