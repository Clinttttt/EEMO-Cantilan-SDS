using System.Globalization;
using System.Numerics;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Identity allocation only; never assesses or prices a space.</summary>
internal sealed class SpaceAccountNumbering(IEnumerable<(ObligationKind Kind, string Label, LotRentalEvent? Event, DateOnly? Date)> existing)
{
    private readonly HashSet<string> used = existing.Select(x => Key(x.Kind, x.Label, x.Event, x.Date)).ToHashSet();

    private static string Scope(ObligationKind kind, LotRentalEvent? ev, DateOnly? date) => $"{(int)kind}|{ev}|{date:yyyy-MM-dd}|";
    private static string Normalize(string label) => Domain.Entities.Revenue.SpaceRegistration.Normalize(label); // one rule for the allocator and the registration
    private static string Key(ObligationKind kind, string label, LotRentalEvent? ev, DateOnly? date) => Scope(kind, ev, date) + Normalize(label);

    public string Next(ObligationKind kind, LotRentalEvent? ev, DateOnly? date)
    {
        var prefix = Scope(kind, ev, date);
        var max = used.Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
            .Select(x => BigInteger.TryParse(x[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : BigInteger.Zero).DefaultIfEmpty().Max();
        return (max + 1).ToString(CultureInfo.InvariantCulture);
    }

    public bool Add(ObligationKind kind, string label, LotRentalEvent? ev, DateOnly? date) => used.Add(Key(kind, label, ev, date));
}
