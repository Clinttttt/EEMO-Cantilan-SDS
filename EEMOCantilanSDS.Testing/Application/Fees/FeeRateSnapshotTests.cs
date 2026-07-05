using System;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Fees
{
    /// <summary>
    /// Phase 4B slice 0 — the fee-rate snapshot. Proves that (a) with no rows the snapshot returns the exact
    /// ordinance constants (so Cantilan is byte-for-byte unchanged before any rewire), (b) a data row overrides
    /// the constant, and (c) effective-dating returns the latest rate on or before a date.
    /// </summary>
    public class FeeRateSnapshotTests
    {
        private static readonly DateOnly AsOf = new(2026, 6, 15);

        [Theory]
        [InlineData(FeeRateKey.NpmDailyStall)]
        [InlineData(FeeRateKey.NpmFishPerKilo)]
        [InlineData(FeeRateKey.SlhHogPerHead)]
        [InlineData(FeeRateKey.SlhLargePerHead)]
        [InlineData(FeeRateKey.TpmVendorDay)]
        [InlineData(FeeRateKey.TrmPerTrip)]
        public void EmptySnapshot_FallsBackToOrdinanceConstant(FeeRateKey key)
        {
            var snapshot = new FeeRateSnapshot(Array.Empty<FeeRateEntry>());

            Assert.Equal(FeeRateDefaults.For(key), snapshot.Resolve(key, AsOf));
        }

        [Fact]
        public void Fallback_ReproducesTodaysCantilanAmounts()
        {
            var snapshot = new FeeRateSnapshot(Array.Empty<FeeRateEntry>());

            Assert.Equal(30.00m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
            Assert.Equal(1.00m, snapshot.Resolve(FeeRateKey.NpmFishPerKilo, AsOf));
            Assert.Equal(250.00m, snapshot.Resolve(FeeRateKey.SlhHogPerHead, AsOf));
            Assert.Equal(365.00m, snapshot.Resolve(FeeRateKey.SlhLargePerHead, AsOf));
            Assert.Equal(100.00m, snapshot.Resolve(FeeRateKey.TpmVendorDay, AsOf));
            Assert.Equal(30.00m, snapshot.Resolve(FeeRateKey.TrmPerTrip, AsOf));
        }

        [Fact]
        public void SeededRow_OverridesTheConstant()
        {
            var snapshot = new FeeRateSnapshot(new[]
            {
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25.00m, new DateOnly(2020, 1, 1)),
            });

            Assert.Equal(25.00m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
        }

        [Fact]
        public void Resolve_ReturnsLatestRateOnOrBeforeDate()
        {
            var snapshot = new FeeRateSnapshot(new[]
            {
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30.00m, new DateOnly(2020, 1, 1)),
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35.00m, new DateOnly(2026, 1, 1)),
            });

            // Before the increase -> old rate; on/after -> new rate.
            Assert.Equal(30.00m, snapshot.Resolve(FeeRateKey.NpmDailyStall, new DateOnly(2025, 12, 31)));
            Assert.Equal(35.00m, snapshot.Resolve(FeeRateKey.NpmDailyStall, new DateOnly(2026, 1, 1)));
            Assert.Equal(35.00m, snapshot.Resolve(FeeRateKey.NpmDailyStall, new DateOnly(2026, 6, 15)));
        }

        [Fact]
        public void Resolve_IgnoresFutureEffectiveDates()
        {
            var snapshot = new FeeRateSnapshot(new[]
            {
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 99.00m, new DateOnly(2030, 1, 1)),
            });

            // The only row is not yet effective -> fall back to the constant, not the future amount.
            Assert.Equal(FeeRateDefaults.For(FeeRateKey.NpmDailyStall), snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
        }
    }
}
