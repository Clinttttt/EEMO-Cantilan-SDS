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
        public static async Task<bool> IsAvailableAsync(
            AppDbContext context,
            string orNumber,
            CancellationToken ct,
            Guid? excludeUtilityBillId = null,
            (string OwnerName, DateOnly Date)? allowSlaughterReceipt = null)
        {
            var or = (orNumber ?? string.Empty).Trim();
            if (or.Length == 0) return true;

            var mid = context.CurrentMunicipalityId;

            if (await context.PaymentRecords.IgnoreQueryFilters()
                    .AnyAsync(p => (mid == Guid.Empty || p.MunicipalityId == mid) && p.ORNumber == or, ct)) return false;
            if (await context.DailyCollections.IgnoreQueryFilters()
                    .AnyAsync(d => (mid == Guid.Empty || d.MunicipalityId == mid) && d.ORNumber == or, ct)) return false;
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
