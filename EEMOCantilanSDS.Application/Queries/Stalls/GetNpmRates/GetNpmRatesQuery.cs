using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;

/// <summary>Returns the caller LGU's currently-effective NPM daily + fish rates (resolved snapshot).</summary>
public record GetNpmRatesQuery() : IRequest<Result<NpmRatesDto>>;
