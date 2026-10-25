using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Reads a renewed space account term by term. A space renewal appends a <see cref="SpaceContractTerm"/> to the SAME account, so its
/// periods, rates and allocations stay exactly as recorded; this only says which term each period belongs to. A period belongs to the
/// latest term that had commenced by the period's start, so the month a term ends in, and any lapsed months before the renewal, stay with
/// the term that ran out — the same rule as the stall register, where the old term keeps what it incurred. Every period lands in exactly
/// one term, so the terms always add back to the account's own totals.
/// </summary>
public static class SpaceTermSplit
{
    /// <summary>Appended terms in force order (commencement, then recording time).</summary>
    public static IReadOnlyList<SpaceContractTerm> Ordered(IEnumerable<SpaceContractTerm> terms) =>
        terms.OrderBy(x => x.Commencement).ThenBy(x => x.RecordedAtUtc).ToList();

    /// <summary>When the term in force began, or null when the account was never renewed (it is then read as one term, as before).</summary>
    public static DateOnly? CurrentTermStart(IReadOnlyList<SpaceContractTerm> ordered) => ordered.Count == 0 ? null : ordered[^1].Commencement;

    /// <summary>
    /// The account's terms, oldest first, each with its own assessed, collected and remaining amounts. Null when the account has no appended
    /// term: a never-renewed account keeps being read as a single term by every existing screen.
    /// </summary>
    public static IReadOnlyList<SpaceTermDto>? Split(ObligationAccount account, IReadOnlyList<SpaceContractTerm> ordered, IReadOnlyList<ObligationQuoteDto> quotes)
    {
        if (ordered.Count == 0) return null;
        var starts = new List<(DateOnly Start, int? Years, string? Name, string? Reference)>
            { (account.ContractEffectiveDate ?? account.ActiveFrom, account.ContractYears, account.ContractName, account.ContractReference) };
        starts.AddRange(ordered.Select(t => (t.Commencement, (int?)t.Years, t.ContractName, t.Reference)));

        var result = new List<SpaceTermDto>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var (start, years, name, reference) = starts[i];
            DateOnly? next = i + 1 < starts.Count ? starts[i + 1].Start : null;
            // The first term also owns any period before its recorded effectivity (an account opened ahead of its paper term).
            var own = quotes.Where(q => (i == 0 || q.PeriodStart >= start) && (next is null || q.PeriodStart < next)).ToList();
            result.Add(new SpaceTermDto(i + 1, start, years, years is { } y ? DomainRules.TermLastDay(start, y) : null, next?.AddDays(-1),
                name, reference, own.Sum(q => q.AssessedAmount), own.Sum(q => q.SettledAmount), own.Sum(q => q.OutstandingAmount), next is null));
        }
        return result;
    }
}
