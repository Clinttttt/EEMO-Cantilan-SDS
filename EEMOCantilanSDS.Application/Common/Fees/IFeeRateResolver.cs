using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Fees
{
    /// <summary>
    /// Resolves the fixed ordinance fee rates for the <b>current municipality</b> (Phase 4B). Loads the
    /// tenant's <c>FacilityRate</c> rows once (already scoped by the global query filter) into an immutable
    /// <see cref="FeeRateSnapshot"/>; callers read amounts from the snapshot as plain locals so the values can
    /// be embedded in EF LINQ.
    ///
    /// <para>
    /// A rate an office has not stated is NOT borrowed from anywhere. It used to fall back to a
    /// constant taken from the reference municipality's own
    /// ordinance: Madrid, which had never stated a per-kilo weighing fee, was therefore charging Cantilan's
    /// ₱1.00 per kilo. Each LGU bills under its own ordinance, so an unstated rate now resolves to nothing at
    /// all, and the paths that would create a charge from one refuse instead of inventing a figure.
    /// </para>
    /// </summary>
    public interface IFeeRateResolver
    {
        Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>One effective-dated fixed rate row for the current tenant.</summary>
    public readonly record struct FeeRateEntry(FacilityCode Facility, FeeRateKey Key, decimal Amount, DateOnly EffectiveDate);

    /// <summary>
    /// Immutable point-in-time view of the current tenant's fixed rates.
    ///
    /// <para>
    /// <see cref="ResolveOrNull"/> answers what the office has STATED, and nothing where it has stated nothing,
    /// which is the distinction every money decision needs. <see cref="Resolve"/> is the same reading with an
    /// unstated rate read as zero, for the screens and reports that must show a figure: zero states plainly that
    /// the office charges nothing under this head, where the old fallback stated another municipality's amount.
    /// </para>
    /// </summary>
    public sealed class FeeRateSnapshot
    {
        private readonly IReadOnlyList<FeeRateEntry> _entries;

        public FeeRateSnapshot(IEnumerable<FeeRateEntry> entries)
            => _entries = entries?.ToList() ?? new List<FeeRateEntry>();

        /// <summary>
        /// The amount this office has STATED for a rate key as of a date (the latest row with
        /// <c>EffectiveDate</c> on or before it), or <c>null</c> where it has stated none. Anything that would
        /// create a charge asks this and refuses on null, rather than billing a figure the office never set.
        /// </summary>
        public decimal? ResolveOrNull(FeeRateKey key, DateOnly asOf)
        {
            var owner = FacilityRateKeys.OwnerOf(key);

            decimal? match = null;
            DateOnly bestDate = DateOnly.MinValue;
            foreach (var e in _entries)
            {
                // A key belongs to one facility's ordinance. A row filed against another facility is a
                // mis-filed row, not that key's rate: reading it would hand one facility's figure to another,
                // which is the same class of error as a hardcoded rate. The write path refuses to create such a
                // row; this refuses to trust one that already exists.
                if (e.Facility != owner || e.Key != key || e.EffectiveDate > asOf) continue;
                if (match is null || e.EffectiveDate >= bestDate)
                {
                    match = e.Amount;
                    bestDate = e.EffectiveDate;
                }
            }
            return match;
        }

        /// <summary>
        /// The stated amount, or zero where this office has stated none.
        ///
        /// <para>
        /// Zero, and deliberately not a constant. The constants are the reference municipality's ordinance, and
        /// returning one here is how another LGU came to charge Cantilan's per-kilo weighing fee. Zero says the
        /// office charges nothing under this head, which is the truth about an office that has stated nothing.
        /// Callers that must not proceed on nothing use <see cref="ResolveOrNull"/>.
        /// </para>
        /// </summary>
        public decimal Resolve(FeeRateKey key, DateOnly asOf) => ResolveOrNull(key, asOf) ?? 0m;
    }
}
