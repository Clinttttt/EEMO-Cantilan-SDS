using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetNpmMeatWeighingRateQuote;

public sealed record GetNpmMeatWeighingRateQuoteQuery(DateOnly BusinessDate)
    : IRequest<Result<NpmMeatWeighingRateQuoteDto>>;
