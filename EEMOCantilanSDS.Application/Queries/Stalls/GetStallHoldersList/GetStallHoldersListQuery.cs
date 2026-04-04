using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

public record GetStallHoldersListQuery(
    FacilityCode FacilityCode,
    MarketSection? Section = null,
    string? SearchTerm = null
) : IRequest<Result<StallHoldersListDto>>;
