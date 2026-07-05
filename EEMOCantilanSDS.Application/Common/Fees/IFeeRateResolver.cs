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
    /// be embedded in EF LINQ. When a tenant has no row for a key, the snapshot falls back to the
    /// <see cref="FeeRateDefaults"/> constant, so Cantilan (seeded from those constants) is byte-for-byte
    /// unchanged.
    /// </summary>
    public interface IFeeRateResolver
    {
        Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>One effective-dated fixed rate row for the current tenant.</summary>
    public readonly record struct FeeRateEntry(FacilityCode Facility, FeeRateKey Key, decimal Amount, DateOnly EffectiveDate);

    /// <summary>
    /// Immutable point-in-time view of the current tenant's fixed rates. <see cref="Resolve"/> returns the
    /// amount in effect on a date (the latest row with <c>EffectiveDate</c> on or before it), or the
    /// <see cref="FeeRateDefaults"/> constant when the tenant has no row for that key.
    /// </summary>
    public sealed class FeeRateSnapshot
    {
        private readonly IReadOnlyList<FeeRateEntry> _entries;

        public FeeRateSnapshot(IEnumerable<FeeRateEntry> entries)
            => _entries = entries?.ToList() ?? new List<FeeRateEntry>();

        /// <summary>The amount for a fixed rate key as of a date, falling back to the ordinance constant.</summary>
        public decimal Resolve(FeeRateKey key, DateOnly asOf)
        {
            decimal? match = null;
            DateOnly bestDate = DateOnly.MinValue;
            foreach (var e in _entries)
            {
                if (e.Key != key || e.EffectiveDate > asOf) continue;
                if (match is null || e.EffectiveDate >= bestDate)
                {
                    match = e.Amount;
                    bestDate = e.EffectiveDate;
                }
            }
            return match ?? FeeRateDefaults.For(key);
        }
    }
}
