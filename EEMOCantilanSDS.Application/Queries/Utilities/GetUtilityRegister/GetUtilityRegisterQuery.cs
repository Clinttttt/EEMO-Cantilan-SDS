using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;

/// <summary>The end-of-month electricity &amp; water billing register for the active NPM stalls.</summary>
public record GetUtilityRegisterQuery(int Year, int Month, MarketSection? Section)
    : IRequest<Result<UtilityRegisterDto>>;
