using System;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Fees
{
    /// <summary>
    /// The fee-rate snapshot: what an office charges, and what it means for an office to have stated nothing.
    ///
    /// <para>
    /// A rate an office has not stated used to fall back to a <c>FeeRates</c> constant. Those constants are the
    /// reference municipality's own ordinance, so the fallback billed one LGU's figures to another: Madrid, which
    /// had never stated a per-kilo weighing fee, was charging Cantilan's ₱1.00 per kilo on its own vendors' fish.
    /// Each LGU collects under its own ordinance, so an unstated rate now resolves to nothing at all.
    /// </para>
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
        public void AnUnstatedRateIsNothing_NotAnotherMunicipalitysFigure(FeeRateKey key)
        {
            var snapshot = new FeeRateSnapshot(Array.Empty<FeeRateEntry>());

            Assert.Null(snapshot.ResolveOrNull(key, AsOf));
            Assert.Equal(0m, snapshot.Resolve(key, AsOf));
        }

        [Fact]
        public void NoneOfTheReferenceMunicipalitysAmountsReachAnOfficeThatStatedNothing()
        {
            // Named for the amounts themselves, because these are the figures that were being charged elsewhere:
            // ₱30 a day, ₱1 a kilo, ₱250 a hog, ₱365 a large animal, ₱100 a market-day vendor, ₱30 a trip.
            var snapshot = new FeeRateSnapshot(Array.Empty<FeeRateEntry>());

            foreach (var key in new[]
                     {
                         FeeRateKey.NpmDailyStall, FeeRateKey.NpmFishPerKilo, FeeRateKey.SlhHogPerHead,
                         FeeRateKey.SlhLargePerHead, FeeRateKey.TpmVendorDay, FeeRateKey.TrmPerTrip,
                     })
            {
                Assert.Equal(0m, snapshot.Resolve(key, AsOf));
            }
        }

        [Fact]
        public void AStatedRateIsWhatTheOfficeCharges()
        {
            var snapshot = new FeeRateSnapshot(new[]
            {
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 25.00m, new DateOnly(2020, 1, 1)),
            });

            Assert.Equal(25.00m, snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, AsOf));
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
        public void ARateThatTakesEffectLaterIsNotInForceYet()
        {
            var snapshot = new FeeRateSnapshot(new[]
            {
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 99.00m, new DateOnly(2030, 1, 1)),
            });

            // The office has stated a rate, but not for this date. Nothing is in force, and the future amount is
            // certainly not: billing today at a rate that begins in 2030 would charge what nobody has approved.
            Assert.Null(snapshot.ResolveOrNull(FeeRateKey.NpmDailyStall, AsOf));
            Assert.Equal(0m, snapshot.Resolve(FeeRateKey.NpmDailyStall, AsOf));
        }
    }
}
