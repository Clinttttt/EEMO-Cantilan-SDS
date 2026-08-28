using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    /// <summary>
    /// The daily fee an office states for ONE OF ITS OWN market sections, per municipality and effective-dated.
    ///
    /// <para>
    /// The three areas the platform starts with are priced through <see cref="FacilityRate"/>, because each of them is a
    /// known key of the ordinance. A market's own section is not: its name is whatever that LGU calls it, so it cannot be
    /// an enum value and cannot be a rate key. Until now the office priced such a section one stall at a time, typing the
    /// same figure for every stall it recorded there, and a section it had not yet put anybody in could not be priced at
    /// all.
    /// </para>
    ///
    /// <para>
    /// Effective-dated for the reason every rate here is: the fee in force for a date is the latest row with
    /// <see cref="EffectiveDate"/> on or before it, so stating a rate today leaves every earlier day exactly as it was
    /// billed. A stall let at its own rate keeps that rate — the office's ruling, and the order is stated once in
    /// <c>NpmDailyFee</c>.
    /// </para>
    /// </summary>
    public class FacilitySectionRate : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        /// <summary>The facility whose section this is. A rate belongs to one facility's ordinance, as every rate here does.</summary>
        public FacilityCode FacilityCode { get; private set; }

        /// <summary>The office's own name for the section, trimmed. Matched case-insensitively, as section names are everywhere.</summary>
        public string SectionName { get; private set; } = string.Empty;

        public decimal Amount { get; private set; }
        public DateOnly EffectiveDate { get; private set; }

        private FacilitySectionRate() { }

        /// <summary>
        /// Adjusts the amount in place, for an edit landing on a row that already exists for that effective date — the
        /// same day, in practice. History under other dates is untouched.
        /// </summary>
        public void UpdateAmount(decimal amount, string updatedBy = "System")
        {
            Amount = amount;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public static FacilitySectionRate Create(
            FacilityCode facilityCode,
            string sectionName,
            decimal amount,
            DateOnly effectiveDate,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            var name = (sectionName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new ArgumentException("A section rate must name the section it prices.", nameof(sectionName));
            if (amount < 0m)
                throw new ArgumentOutOfRangeException(nameof(amount), "A section's daily fee cannot be negative.");

            return new FacilitySectionRate
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityCode = facilityCode,
                SectionName = name,
                Amount = amount,
                EffectiveDate = effectiveDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
