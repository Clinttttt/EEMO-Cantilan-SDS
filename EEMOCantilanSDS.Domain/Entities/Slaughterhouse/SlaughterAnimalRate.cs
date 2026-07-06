using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Slaughterhouse
{
    /// <summary>
    /// A per-LGU registry of custom slaughterhouse animal types and their default per-head rate. Seeded at
    /// activation from the LGU's onboarding config and editable later in the portal. The built-in animals
    /// (Hog/Carabao/Cow) keep their rates in <c>FeeRates</c>/<c>FacilityRates</c>; this table only holds an
    /// LGU's extra ("Other") animals (e.g. Goat, Chicken) so the SLH record screen can offer them with a
    /// default rate the admin can still override per transaction (<see cref="AnimalType.Other"/>).
    /// </summary>
    public class SlaughterAnimalRate : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        /// <summary>The custom animal's display name (unique per LGU, case-insensitive by convention).</summary>
        public string AnimalName { get; private set; } = string.Empty;

        /// <summary>Default per-head fee suggested when recording this animal (admin may override).</summary>
        public decimal RatePerHead { get; private set; }

        /// <summary>Soft on/off toggle so an LGU can retire an animal without deleting its history.</summary>
        public bool IsActive { get; private set; } = true;

        private SlaughterAnimalRate() { }

        public static SlaughterAnimalRate Create(
            string animalName,
            decimal ratePerHead,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            return new SlaughterAnimalRate
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                AnimalName = animalName.Trim(),
                RatePerHead = ratePerHead,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>Adjusts the default per-head rate (portal self-service edit).</summary>
        public void UpdateRate(decimal ratePerHead, string updatedBy = "System")
        {
            RatePerHead = ratePerHead;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>Retires or re-enables the animal type without deleting it.</summary>
        public void SetActive(bool isActive, string updatedBy = "System")
        {
            IsActive = isActive;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
