using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.DailyCollections.QuoteNpmAmount;

public class QuoteNpmAmountQueryHandler(IStallRepository stallRepository, INpmMonthSettlementService settlement)
    : IRequestHandler<QuoteNpmAmountQuery, Result<NpmAmountQuoteDto>>
{
    public async Task<Result<NpmAmountQuoteDto>> Handle(QuoteNpmAmountQuery request, CancellationToken ct)
    {
        if (request.Amount <= 0m || request.Amount != decimal.Round(request.Amount, 2))
            return Result<NpmAmountQuoteDto>.Failure("Enter an amount in pesos and centavos greater than zero.", ResultStatus.Invalid);
        if (request.Month is < 1 or > 12 || request.Year is < 1990 or > 2200)
            return Result<NpmAmountQuoteDto>.Failure("That is not a real month.", ResultStatus.Invalid);

        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall is null) return Result<NpmAmountQuoteDto>.NotFound();
        if (stall.Facility?.Code != FacilityCode.NPM || stall.Facility.IsMonthlyRentMarket)
            return Result<NpmAmountQuoteDto>.Failure("Only New Public Market (daily) accounts are quoted by day.", ResultStatus.Invalid);

        var days = (await settlement.GetPayableDaysAsync(stall, request.Year, request.Month, ct)).OrderBy(d => d).ToList();

        // What each whole number of oldest days comes to — the same quote settlement itself uses, month-end adjustment and the
        // month's ceiling included, so the amount shown here is exactly what settling those days records.
        var steps = new List<(int Count, decimal Amount)>();
        for (var n = 1; n <= days.Count; n++)
        {
            var quote = await settlement.ComputePayableForDaysAsync(stall, request.Year, request.Month, n, ct);
            if (quote.Days != n || quote.Amount <= 0m) break;   // a closed short month answers nothing for a part of it
            if (steps.Count > 0 && quote.Amount <= steps[^1].Amount) continue;
            steps.Add((n, quote.Amount));
        }

        var total = steps.Count == 0 ? 0m : steps[^1].Amount;
        decimal? below = steps.Where(s => s.Amount < request.Amount).Select(s => (decimal?)s.Amount).LastOrDefault();
        decimal? above = steps.Where(s => s.Amount > request.Amount).Select(s => (decimal?)s.Amount).FirstOrDefault();

        if (steps.Count == 0)
            return Result<NpmAmountQuoteDto>.Success(new(request.Amount, "NothingPayable", [], 0m, request.Amount, null, null, 0m, 0));

        var exact = steps.FirstOrDefault(s => s.Amount == request.Amount);
        if (exact.Count > 0)
            return Result<NpmAmountQuoteDto>.Success(new(request.Amount, "Exact", days.Take(exact.Count).ToList(), request.Amount, 0m, below, above, total, steps[^1].Count));

        var outcome = request.Amount > total ? "ExceedsPayable" : "NotWholeInstallments";
        var allocated = below ?? 0m;
        return Result<NpmAmountQuoteDto>.Success(new(request.Amount, outcome, [], allocated, request.Amount - allocated, below, above, total, steps[^1].Count));
    }
}
