using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.QuoteNpmAmount;

/// <summary>
/// Previews an amount received against an NPM stall's payable days of one month. Allocation is oldest-first and only ever to
/// whole daily installments the stall actually owes; an amount that is not exactly such a sum is reported with the nearest valid
/// amounts and is never turned into a paid day, a partial day or an advance. No mutation.
/// </summary>
public record QuoteNpmAmountQuery(Guid StallId, int Year, int Month, decimal Amount) : IRequest<Result<NpmAmountQuoteDto>>;
