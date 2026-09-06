using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

/// <summary>The official List of Stallholders.</summary>
/// <param name="Year">
/// The year the roster is stated AS OF: null or the current year mean today, an earlier year means its last day. Not a
/// filter on the rows - an earlier year reads the contract history, because this roster otherwise shows only who holds a
/// stall now and could never answer who held it then.
/// </param>
public record GetStallHoldersListQuery(
    FacilityCode FacilityCode,
    MarketSection? Section = null,
    string? SearchTerm = null,
    int? Year = null
) : IRequest<Result<StallHoldersListDto>>;
