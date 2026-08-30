using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    /// <summary>
    /// DORMANT since 2026-08-30. Nothing reads this and nothing writes it.
    ///
    /// <para>
    /// It once held whether stalls in one of the office's own market sections are metered, as a default for a stall being
    /// recorded there. The office asked for the control to be removed and was right: a new market stall's form already
    /// opens with BOTH meters ticked, and the section default could only ever ADD one, so it could add nothing that was
    /// not already there. Its only reachable effect was to re-tick a meter a clerk had just unticked, if they then changed
    /// the section, which is the opposite of a help.
    /// </para>
    ///
    /// <para>
    /// The type and its table remain because production applies migrations at startup and this platform is additive only:
    /// dropping a table would fail a running instance mid-deploy. Keeping them also means rows an office already has are
    /// still carried by its backup and still removed with the tenant. <c>SectionRateReadersAreNamedTests</c> holds it to
    /// exactly that, and will fail if anything starts reading it again.
    /// </para>
    ///
    /// <para>
    /// A stall's meters belong to the SPACE, not to the section it trades in, and always did: a section may be wired
    /// throughout while one stall in it has no connection. That is why this was only ever a default, and why removing it
    /// changes nothing that is billed.
    /// </para>
    /// </summary>
    public class FacilitySectionUtilities : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public FacilityCode FacilityCode { get; private set; }

        /// <summary>The office's own name for the section, trimmed. Matched case-insensitively, as section names are.</summary>
        public string SectionName { get; private set; } = string.Empty;

        public bool Electricity { get; private set; }
        public bool Water { get; private set; }

        private FacilitySectionUtilities() { }

        public void Set(bool electricity, bool water, string updatedBy = "System")
        {
            Electricity = electricity;
            Water = water;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public static FacilitySectionUtilities Create(
            FacilityCode facilityCode,
            string sectionName,
            bool electricity,
            bool water,
            Guid municipalityId = default,
            string createdBy = "System")
        {
            var name = (sectionName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new ArgumentException("A section's utilities must name the section.", nameof(sectionName));

            return new FacilitySectionUtilities
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityCode = facilityCode,
                SectionName = name,
                Electricity = electricity,
                Water = water,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
    }
}
