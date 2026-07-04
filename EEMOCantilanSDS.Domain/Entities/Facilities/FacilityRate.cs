using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    /// <summary>
    /// A fixed ordinance fee rate for a facility, per municipality and effective-dated (Phase 4). Moves the
    /// hard-coded <c>FeeRates</c> constants into data so each LGU can carry its own rates and rate changes
    /// are historically accurate. The rate in effect for a date is the latest row with
    /// <see cref="EffectiveDate"/> on or before that date. Only fixed rates live here — negotiated monthly
    /// rentals stay on <c>Stall.MonthlyRate</c>.
    /// </summary>
    public class FacilityRate : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public FacilityCode FacilityCode { get; private set; }
        public FeeRateKey RateKey { get; private set; }
        public decimal Amount { get; private set; }
        public DateOnly EffectiveDate { get; private set; }

        private FacilityRate() { }

        public static FacilityRate Create(
            FacilityCode facilityCode,
            FeeRateKey rateKey,
            decimal amount,
            DateOnly effectiveDate,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            return new FacilityRate
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityCode = facilityCode,
                RateKey = rateKey,
                Amount = amount,
                EffectiveDate = effectiveDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
