using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories
{
    /// <summary>
    /// Single source of truth for Official-Receipt (OR) number uniqueness across every module. OR numbers
    /// are manually entered from the LGU's receipt booklets and must be unique within the LGU, spanning
    /// monthly rentals, NPM daily collections, slaughterhouse, Tabo-an, transport terminal, and NPM utility
    /// (electricity/water) bills. Uniqueness is checked even against soft-deleted rows (a receipted OR can
    /// never be reused) and is scoped to the current municipality (a second LGU may reuse an OR that only
    /// exists in another LGU). Every repository routes its OR checks here so the module list never drifts.
    /// </summary>
    internal static class OrNumberRegistry
    {
        /// <summary>
        /// True when <paramref name="orNumber"/> is not yet used anywhere in the current LGU.
        /// <paramref name="excludeUtilityBillId"/> lets a utility bill re-mark its own OR.
        /// <paramref name="allowSlaughterReceipt"/> permits the same OR to recur within one slaughterhouse
        /// receipt (same owner + same transaction date, one receipt covering multiple heads).
        /// </summary>
        /// <param name="allowDailyStall">permits the same OR to recur across multiple daily collections
        /// of the SAME NPM stall (one receipt covering several days); it is still rejected when the OR
        /// already belongs to a DIFFERENT stall.</param>
        /// <param name="allowMonthlyStall">permits the same OR to recur across multiple monthly payment
        /// records of the SAME stall (one receipt settling several months, e.g. "all outstanding"); it is
        /// still rejected when the OR already belongs to a DIFFERENT stall's payment record.</param>
        public static async Task<bool> IsAvailableAsync(
            AppDbContext context,
            string orNumber,
            CancellationToken ct,
            Guid? excludeUtilityBillId = null,
            (string OwnerName, DateOnly Date)? allowSlaughterReceipt = null,
            Guid? allowDailyStall = null,
            Guid? allowMonthlyStall = null)
        {
            var or = (orNumber ?? string.Empty).Trim();
            if (or.Length == 0) return true;

            var mid = context.CurrentMunicipalityId;

            // Monthly rentals: one OR may settle multiple months of the SAME stall (one receipt); reject
            // only when the OR already belongs to a DIFFERENT stall's payment record.
            if (allowMonthlyStall is { } monthlyStallId)
            {
                if (await context.PaymentRecords.IgnoreQueryFilters()
                        .AnyAsync(p => (mid == Guid.Empty || p.MunicipalityId == mid) && p.ORNumber == or && p.StallId != monthlyStallId, ct)) return false;
            }
            else
            {
                if (await context.PaymentRecords.IgnoreQueryFilters()
                        .AnyAsync(p => (mid == Guid.Empty || p.MunicipalityId == mid) && p.ORNumber == or, ct)) return false;
            }

            // NPM daily: one OR may cover multiple days of the SAME stall (one receipt, several days);
            // reject only when the OR already belongs to a DIFFERENT stall's daily collection.
            if (allowDailyStall is { } dailyStallId)
            {
                if (await context.DailyCollections.IgnoreQueryFilters()
                        .AnyAsync(d => (mid == Guid.Empty || d.MunicipalityId == mid) && d.ORNumber == or && d.StallId != dailyStallId, ct)) return false;
            }
            else
            {
                if (await context.DailyCollections.IgnoreQueryFilters()
                        .AnyAsync(d => (mid == Guid.Empty || d.MunicipalityId == mid) && d.ORNumber == or, ct)) return false;
            }

            if (await context.TpmAttendances.IgnoreQueryFilters()
                    .AnyAsync(a => (mid == Guid.Empty || a.MunicipalityId == mid) && a.ORNumber == or, ct)) return false;
            if (await context.TrmTrips.IgnoreQueryFilters()
                    .AnyAsync(t => (mid == Guid.Empty || t.MunicipalityId == mid) && t.ORNumber == or, ct)) return false;
            if (await context.UtilityBills.IgnoreQueryFilters()
                    .AnyAsync(b => (mid == Guid.Empty || b.MunicipalityId == mid)
                        && (excludeUtilityBillId == null || b.Id != excludeUtilityBillId)
                        && ((b.ElecORNumber != null && b.ElecORNumber == or) || (b.WaterORNumber != null && b.WaterORNumber == or)), ct)) return false;

            // Slaughterhouse: within a single receipt (same owner + same date) the OR may repeat; reject only
            // when it already belongs to a different owner or a different transaction date.
            if (allowSlaughterReceipt is { } receipt)
            {
                if (await context.SlaughterTransactions.IgnoreQueryFilters()
                        .AnyAsync(s => (mid == Guid.Empty || s.MunicipalityId == mid) && s.ORNumber == or
                            && (s.OwnerName != receipt.OwnerName || s.TransactionDate != receipt.Date), ct)) return false;
            }
            else
            {
                if (await context.SlaughterTransactions.IgnoreQueryFilters()
                        .AnyAsync(s => (mid == Guid.Empty || s.MunicipalityId == mid) && s.ORNumber == or, ct)) return false;
            }

            return true;
        }
    }
}
