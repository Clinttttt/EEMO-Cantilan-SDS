using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Presents a space account's assessed periods as the monthly payment records the stall calendar reads, so the same calendar draws both. Every figure is the
/// period register's own (assessed, settled, outstanding); an unassessed period carries no record and so shows as a month with nothing to collect.
/// </summary>
public static class SpacePaymentMonths
{
    public static List<PaymentHistoryDto> From(IEnumerable<ObligationQuoteDto> periods) => periods
        .Where(p => p.AssessedAmount > 0m)
        .GroupBy(p => p.PeriodStart)
        .Select(g => g.First())
        .Select(p => new PaymentHistoryDto(
            p.PeriodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            p.OutstandingAmount <= 0m ? PaymentStatus.Paid : p.SettledAmount > 0m ? PaymentStatus.Partial : PaymentStatus.Unpaid,
            p.AssessedAmount, p.SettledAmount, p.OutstandingAmount, null, null))
        .ToList();
}
