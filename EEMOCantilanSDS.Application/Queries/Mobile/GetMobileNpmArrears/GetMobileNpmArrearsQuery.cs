using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmArrears;

/// <summary>
/// What the market is behind on, as of the round's month.
/// </summary>
/// <remarks>
/// Its own query rather than more fields on the round. The round is fetched at every stall and must stay light; this walks each
/// unsettled month of every payor and asks the office's settlement to price it, so it is fetched once, when the collector opens
/// the "Days still owed" screen.
/// </remarks>
public record GetMobileNpmArrearsQuery(int Year, int Month) : IRequest<Result<MobileNpmArrearsDto>>;
