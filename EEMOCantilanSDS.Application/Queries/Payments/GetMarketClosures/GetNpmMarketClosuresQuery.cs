using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMarketClosures;

/// <summary>Returns the closed day numbers (1–31) of the NPM market for a calendar month.</summary>
public record GetNpmMarketClosuresQuery(int Year, int Month) : IRequest<Result<IReadOnlyList<int>>>;
