using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Facility : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public FacilityCode Code { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string ShortName { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public bool IsActive { get; private set; } = true;

        /// <summary>How this facility bills, independent of its <see cref="Code"/> (Phase 4). Stored as data
        /// so another LGU can map its facilities to any billing behaviour.</summary>
        public BillingArchetype Archetype { get; private set; } = BillingArchetype.Custom;

        public ICollection<Stall> Stalls { get; private set; } = new List<Stall>();
        public ICollection<CollectorFacilityAssignment> CollectorAssignments { get; private set; } = new List<CollectorFacilityAssignment>();
        private Facility() { }
        public static Facility Create(
           FacilityCode code,
           string name,
           string shortName,
           string? description = null,
           BillingArchetype? archetype = null,
           Guid municipalityId = default)
        {
            return new Facility
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                Code = code,
                Name = name,
                ShortName = shortName,
                Description = description,
                Archetype = archetype ?? DefaultArchetypeFor(code),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }

        /// <summary>Cantilan's facility→archetype mapping, used as the seed/default. The stored column is
        /// the source of truth, so this only supplies a sensible default when none is provided.</summary>
        public static BillingArchetype DefaultArchetypeFor(FacilityCode code) => code switch
        {
            FacilityCode.NPM => BillingArchetype.DailyStall,
            FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE => BillingArchetype.MonthlyRental,
            FacilityCode.SLH => BillingArchetype.PerHead,
            FacilityCode.TRM => BillingArchetype.PerTrip,
            FacilityCode.TPM => BillingArchetype.WeeklyMarket,
            _ => BillingArchetype.Custom
        };

        public void Deactivate() => IsActive = false;
        public void Activate() => IsActive = true;

        /// <summary>
        /// Updates the facility's presentation (name, short name, description). The <see cref="Code"/> and
        /// <see cref="Archetype"/> are immutable — they anchor the collection/report machinery — so only the
        /// labels change. Lets a Head correct an onboarding naming artifact without re-onboarding.
        /// </summary>
        public void UpdateProfile(string name, string shortName, string? description, string updatedBy = "System")
        {
            Name = name.Trim();
            ShortName = shortName.Trim();
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
