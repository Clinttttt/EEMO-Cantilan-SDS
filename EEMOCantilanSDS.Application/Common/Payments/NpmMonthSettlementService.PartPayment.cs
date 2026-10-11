using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>The days a part payment has started to settle: what each still owes and what it has received.</summary>
public readonly record struct NpmPartPaidPosition(IReadOnlyList<(DateOnly Day, decimal Remaining, decimal Settled)> Days, decimal Leftover);

/// <summary>One installment a part payment reaches: its fee, what was already received, what this payment applies and what is left.</summary>
public sealed record NpmPartLine(DateOnly Day, decimal Fee, decimal Settled, decimal Applied, decimal Remaining)
{
    public string Status => Remaining == 0m ? "Paid" : Settled + Applied > 0m ? "Partial" : "Unpaid";
}

/// <summary>
/// What a payment received for an NPM stall's month does, by the month's own rules: applied oldest-first to installments that
/// are payable today, never beyond the month's ceiling and never as an advance. <see cref="Outcome"/> is "Applied",
/// "ExceedsPayable", "NothingPayable" or "Invalid"; only "Applied" may be recorded. <see cref="Token"/> fingerprints the facts the
/// plan rests on, so a payment recorded later can tell that the month changed under it.
/// </summary>
public sealed record NpmPartPaymentPlan(string Outcome, string? Message, decimal Amount, decimal Applied, decimal Payable,
    decimal Adjustment, decimal Unallocated, IReadOnlyList<NpmPartLine> Lines, string Token)
{
    /// <summary>What the month still owes in installments once this payment is applied.</summary>
    public decimal RemainingAfter => Outcome == "Applied" ? Payable - Applied : Payable;
}

public sealed partial class NpmMonthSettlementService
{
    /// <inheritdoc />
    public async Task<NpmPartPaymentPlan> PlanPartPaymentAsync(Stall stall, int year, int month, decimal amount, CancellationToken ct)
    {
        NpmPartPaidPosition part = new([], 0m);
        var (days, remaining, adjustment, _) = await ResolveMonthAsync(stall, year, month, ct, capturePartial: p => part = p);

        // Every installment payable today, oldest first: the whole-fee days and the part-paid days, which owe only what is left.
        var due = days.Select(d => (d.Day, Fee: d.Fee, Settled: 0m, Owes: d.Fee))
            .Concat(part.Days.Select(p => (p.Day, Fee: p.Remaining + p.Settled, p.Settled, Owes: p.Remaining)))
            .OrderBy(x => x.Day).ToList();

        // The month-end adjustment is not an installment: it is collected as the month's final, whole payment, never in parts.
        var ceiling = remaining - adjustment;
        var payable = new List<(DateOnly Day, decimal Fee, decimal Settled, decimal Owes)>();
        var total = 0m;
        foreach (var item in due)
        {
            if (total + item.Owes > ceiling) break;                 // a day beyond the month's ceiling is left alone
            total += item.Owes;
            payable.Add(item);
        }

        var facts = string.Join("|", payable.Select(p => FormattableString.Invariant($"{p.Day:yyyy-MM-dd}:{p.Fee}:{p.Settled}")))
            + FormattableString.Invariant($"|{amount}|{adjustment}");
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)));

        NpmPartPaymentPlan Refused(string outcome, string message) =>
            new(outcome, message, amount, 0m, total, adjustment, amount, [], token);

        if (amount <= 0m || amount != decimal.Round(amount, 2))
            return Refused("Invalid", "Enter an amount in pesos and centavos greater than zero.");
        if (total == 0m)
            return Refused("NothingPayable", "Nothing is payable for this month today.");
        if (amount > total)
            return Refused("ExceedsPayable", FormattableString.Invariant($"The month owes ₱{total:N2} today. A larger amount is not kept as an advance."));

        var left = amount;
        var lines = new List<NpmPartLine>();
        foreach (var item in payable)
        {
            if (left == 0m) break;
            var applied = Math.Min(left, item.Owes);
            left -= applied;
            lines.Add(new NpmPartLine(item.Day, item.Fee, item.Settled, applied, item.Owes - applied));
        }
        return new NpmPartPaymentPlan("Applied", null, amount, amount, total, adjustment, 0m, lines, token);
    }
}
