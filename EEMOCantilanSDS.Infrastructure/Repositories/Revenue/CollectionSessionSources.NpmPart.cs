using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

// Partial of CollectionSessionSources: an NPM payment of an AMOUNT against this month's installments, under one occupancy source. The
// server plans how the amount is applied (full or part installments, oldest first, never past what the month owes) and the item quote is
// that plan; posting re-plans and records exactly the reviewed amount through the same canonical poster as every other NPM payment.
public sealed partial class CollectionSessionSources
{
    private async Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteNpmPartAsync(
        CollectionSessionIntent session, CollectionSessionItemIntent item, CancellationToken ct)
    {
        (CollectionSessionItemQuote?, CollectionSessionProblem?) Fail(string code, string message) => (null, new(item.ClientItemId, code, message));
        var part = item.NpmPart!;
        // This month only, under the one occupancy source: a part payment is never an advance and never another month's money.
        if (npmPart is null || item.Kind != CollectionSessionItemKind.NpmPartPayment || session.SourceIdentity is null
            || part.Year != session.BusinessDate.Year || part.Month != session.BusinessDate.Month)
            return Fail("InvalidIntent", "Choose this month's part payment.");
        var result = await npmPart.QuoteAsync(part.StallId, part.Year, part.Month, item.ConfirmedAmount, ct, session.BusinessDate);
        if (!result.IsSuccess || result.Value is not { } quote || quote.Instrument is not { } instrument)
            return Fail("SourceNotAvailable", "Part payments are not available for this stall.");
        if (quote.Outcome != "Applied")
            return Fail(quote.Outcome == "ExceedsPayable" ? "AmountExceedsBalance" : quote.Outcome == "NothingPayable" ? "NothingPayable" : "InvalidIntent",
                quote.Message ?? "This amount cannot be applied.");
        var stallNo = await db.Stalls.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Id == part.StallId).Select(x => x.StallNo).SingleOrDefaultAsync(ct);
        var days = string.Join(", ", quote.Lines.Select(l => $"{l.Day:MMM d} {l.Status}"));
        var remaining = quote.RemainingAfter > 0m ? $" · ₱{quote.RemainingAfter:N2} still payable this month" : " · month's installments complete";
        return (new(item.ClientItemId, item.Kind, "NPM_PART_PAYMENT", "NPM Part payment", $"Stall {stallNo} · {days}{remaining}",
            instrument, quote.Amount, quote.QuoteToken, Guid.Empty), null);
    }

    private async Task<CollectionSessionCollection> PostNpmPartAsync(CollectionSessionIntent session, CollectionSessionItemIntent item, Guid operation, CancellationToken ct)
    {
        var part = item.NpmPart!;
        var current = npmPart is null ? null : await npmPart.QuoteAsync(part.StallId, part.Year, part.Month, item.ConfirmedAmount, ct, session.BusinessDate);
        if (current?.Value is not { Outcome: "Applied", Instrument: { } instrument } fresh)
            throw new CollectionSessionPostingException(item.ClientItemId, "The rent balance changed. Review the checkout.");
        var posted = await npmPart!.PostAsync(new(operation, part.StallId, part.Year, part.Month, session.BusinessDate, item.ConfirmedAmount, fresh.QuoteToken), ct);
        if (!posted.IsSuccess || posted.Value!.Amount != item.ConfirmedAmount)
            throw new CollectionSessionPostingException(item.ClientItemId, "The rent balance changed. Review the checkout.");
        return new(posted.Value.CollectionId, posted.Value.ReferenceCode, instrument, posted.Value.Amount, [item.ClientItemId], "Posted");
    }
}
