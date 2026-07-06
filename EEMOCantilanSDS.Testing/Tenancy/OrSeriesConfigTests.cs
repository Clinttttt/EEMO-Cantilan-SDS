using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Xunit;

namespace EEMOCantilanSDS.Testing.Tenancy
{
    /// <summary>
    /// OR-series suggestion formatting. OR numbers stay manually entered; these prove the pure
    /// prefix + zero-padding + advance behavior the portal uses to pre-fill a suggestion.
    /// </summary>
    public class OrSeriesConfigTests
    {
        [Fact]
        public void Peek_FormatsPrefixAndZeroPaddedNumber()
        {
            var cfg = OrSeriesConfig.Create("CANT-2026-", 7, 6);
            Assert.Equal("CANT-2026-000007", cfg.Peek());
        }

        [Fact]
        public void Advance_IncrementsAndReturnsNewSuggestion()
        {
            var cfg = OrSeriesConfig.Create(null, 1, 4);
            Assert.Equal("0001", cfg.Peek());

            var next = cfg.Advance();

            Assert.Equal("0002", next);
            Assert.Equal("0002", cfg.Peek());
            Assert.Equal(2, cfg.NextNumber);
        }

        [Fact]
        public void Create_ClampsInvalidValues()
        {
            var cfg = OrSeriesConfig.Create("  ", 0, -3);

            Assert.Null(cfg.Prefix);
            Assert.Equal(1, cfg.NextNumber);
            Assert.Equal(0, cfg.PadWidth);
            Assert.Equal("1", cfg.Peek());
        }
    }
}
