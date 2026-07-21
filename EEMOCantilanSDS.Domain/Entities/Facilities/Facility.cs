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

        // Per-market-section display labels (NPM/market facilities only). Null = use the canonical label
        // ("Vegetable Area" / "Fish Area" / "Meat Area"). The MarketSection enum stays the logical key
        // everywhere; these only change what is SHOWN, so an LGU can call its vegetable area "Gulayan".
        public string? VegetableSectionLabel { get; private set; }
        public string? FishSectionLabel { get; private set; }
        public string? MeatSectionLabel { get; private set; }

        /// <summary>How this facility bills, independent of its <see cref="Code"/> (Phase 4). Stored as data
        /// so another LGU can map its facilities to any billing behaviour.</summary>
        public BillingArchetype Archetype { get; private set; } = BillingArchetype.Custom;

        // Per-LGU CUSTOM NPM section registry. NPM stalls may belong to one of these names (Stall.Section
        // null + Stall.CustomSectionName) instead of a canonical MarketSection. Managed by the Head; a name
        // may exist with zero stalls (so it can be pre-declared / removed while empty). Stored as a text[]
        // column; EMPTY for Cantilan and every existing facility, so behaviour is byte-for-byte unchanged.
        // Names are unique per facility (case-insensitive) and trimmed.
        public List<string> CustomSectionNames { get; private set; } = new();

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
            // Custom facilities are monthly-rental: they reuse the standard stall/contract/payment machinery.
            FacilityCode.Custom1 or FacilityCode.Custom2 or FacilityCode.Custom3
                or FacilityCode.Custom4 or FacilityCode.Custom5 => BillingArchetype.MonthlyRental,
            _ => BillingArchetype.Custom
        };

        public void Deactivate() => IsActive = false;
        public void Activate() => IsActive = true;

        /// <summary>
        /// Adds a custom NPM section name to this facility's registry. Trims and de-duplicates
        /// case-insensitively. Returns false if the name is blank or already present (no-op).
        /// </summary>
        public bool AddCustomSection(string name, string updatedBy = "System")
        {
            var n = (name ?? string.Empty).Trim();
            if (n.Length == 0) return false;
            if (CustomSectionNames.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                return false;
            CustomSectionNames.Add(n);
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            return true;
        }

        /// <summary>
        /// Removes a custom NPM section name from the registry (case-insensitive). Returns false if the
        /// name is not present. Callers MUST ensure no stall still uses the name before removing it.
        /// </summary>
        public bool RemoveCustomSection(string name, string updatedBy = "System")
        {
            var existing = CustomSectionNames.FirstOrDefault(x => string.Equals(x, (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null) return false;
            CustomSectionNames.Remove(existing);
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            return true;
        }

        /// <summary>
        /// Sets the display labels for this facility's market sections (e.g. "Gulayan" for the vegetable
        /// area). A blank/whitespace value clears back to the canonical label. The <see cref="MarketSection"/>
        /// enum remains the logical key — only presentation changes.
        /// </summary>
        public void SetSectionLabels(string? vegetable, string? fish, string? meat, string updatedBy = "System")
        {
            VegetableSectionLabel = Normalize(vegetable);
            FishSectionLabel = Normalize(fish);
            MeatSectionLabel = Normalize(meat);
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;

            static string? Normalize(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        /// <summary>The custom display label for a market section, or null when none is set (caller falls
        /// back to the canonical label). Keeps the enum as the key while allowing a tenant-specific name.</summary>
        public string? SectionLabel(MarketSection section) => section switch
        {
            MarketSection.VegetableArea => VegetableSectionLabel,
            MarketSection.FishSection => FishSectionLabel,
            MarketSection.MeatSection => MeatSectionLabel,
            _ => null
        };

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
