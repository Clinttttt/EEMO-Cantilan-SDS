using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Tenancy
{
    /// <summary>
    /// Per-LGU Official Receipt (OR) series configuration, seeded at activation. OR numbers remain
    /// <b>manually entered</b> by admins (business rule: never auto-generated) — this config only lets the
    /// portal <see cref="Peek"/> a <i>suggested</i> next OR (prefix + zero-padded running number) to
    /// pre-fill the field. The admin may accept or override it; the counter only moves when the portal
    /// explicitly calls <see cref="Advance"/> after a receipt actually used the suggestion. One row per LGU.
    /// </summary>
    public class OrSeriesConfig : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        /// <summary>Optional literal prefix prepended to the number (e.g. "CANT-2026-").</summary>
        public string? Prefix { get; private set; }

        /// <summary>The next running number to suggest.</summary>
        public long NextNumber { get; private set; }

        /// <summary>Zero-pad width for the number (0 = no padding). e.g. width 6, number 1 => "000001".</summary>
        public int PadWidth { get; private set; }

        /// <summary>When false the portal shows no suggestion (admins still type OR numbers freely).</summary>
        public bool IsEnabled { get; private set; } = true;

        private OrSeriesConfig() { }

        public static OrSeriesConfig Create(
            string? prefix,
            long startNumber,
            int padWidth,
            bool isEnabled = true,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            return new OrSeriesConfig
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim(),
                NextNumber = startNumber < 1 ? 1 : startNumber,
                PadWidth = padWidth < 0 ? 0 : padWidth,
                IsEnabled = isEnabled,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>The suggested next OR string (non-consuming). Does not advance the counter.</summary>
        public string Peek() =>
            $"{Prefix}{NextNumber.ToString().PadLeft(PadWidth, '0')}";

        /// <summary>
        /// Advances the counter by one — called only after a receipt actually used the suggestion. Returns
        /// the new (post-advance) suggestion. A no-op-safe guard keeps the number positive.
        /// </summary>
        public string Advance(string updatedBy = "System")
        {
            NextNumber = NextNumber < 1 ? 1 : NextNumber + 1;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            return Peek();
        }

        /// <summary>Portal self-service edit of the series format/counter.</summary>
        public void Update(string? prefix, long nextNumber, int padWidth, bool isEnabled, string updatedBy = "System")
        {
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
            NextNumber = nextNumber < 1 ? 1 : nextNumber;
            PadWidth = padWidth < 0 ? 0 : padWidth;
            IsEnabled = isEnabled;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
