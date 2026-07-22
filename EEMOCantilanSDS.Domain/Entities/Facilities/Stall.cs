using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Facilities
{
    public class Stall : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }

        public Guid FacilityId { get; private set; }
        public string StallNo { get; private set; } = string.Empty;
        public StallStatus Status { get; private set; } = StallStatus.Active;

        // The date the stall was frozen/closed (null when active). Used to excuse the closed span on
        // reopen so a temporary closure never back-bills as arrears.
        public DateOnly? ClosedAt { get; private set; }
        public StallType Type { get; private set; } = StallType.Permanent;
        public ApplicableFees Fees { get; private set; }

        // NPM-specific
        public MarketSection? Section { get; private set; }

        // NPM per-LGU CUSTOM section: when an NPM stall belongs to a section that is NOT one of the three
        // canonical MarketSection values, Section is null and this holds the custom section name (e.g.
        // "Sari-sari Area"). Mirrors how NCC stalls use AreaNote for custom areas. A custom section bills
        // exactly like the Vegetable/Meat sections — flat daily fee, never fish/weight. A stall carries
        // EITHER a canonical Section OR a CustomSectionName, never both.
        public string? CustomSectionName { get; private set; }

        // NCC-specific
        public NccAreaLocation? AreaLocation { get; private set; }

        // Physical info
        public double? AreaSqm { get; private set; }
        public string? AreaNote { get; private set; }
        public string? Remarks { get; private set; }

        // Rates
        public decimal MonthlyRate { get; private set; }
        public decimal? DailyRate { get; private set; }

        public Facility? Facility { get; private set; }
        public ICollection<Contract> Contracts { get; private set; } = new List<Contract>();
        public   ICollection<PaymentRecord> PaymentRecords { get; private set; } = new List<PaymentRecord>();
        public ICollection<DailyCollection> DailyCollections { get; private set; } = new List<DailyCollection>();
        
        private Stall() { }

        public static Stall Create(
            Guid facilityId,
            string stallNo,
            decimal monthlyRate,
            ApplicableFees fees,
            MarketSection? section = null,
            NccAreaLocation? areaLocation = null,
            double? areaSqm = null,
            string? areaNote = null,
            decimal? dailyRate = null,
            string? remarks = null,
            StallType type = StallType.Permanent,
            string createdBy = "System",
            Guid municipalityId = default,
            string? customSectionName = null)
        {
            return new Stall
            {
                Id = Guid.NewGuid(),
                MunicipalityId = municipalityId,
                FacilityId = facilityId,
                StallNo = stallNo,
                MonthlyRate = monthlyRate,
                DailyRate = dailyRate,
                Fees = fees,
                Section = section,
                // A stall is either a canonical Section OR a custom-named section, never both.
                CustomSectionName = section.HasValue || string.IsNullOrWhiteSpace(customSectionName)
                    ? null : customSectionName.Trim(),
                AreaLocation = areaLocation,
                AreaSqm = areaSqm,
                AreaNote = areaNote,
                Remarks = remarks,
                Status = StallStatus.Active,
                Type = type,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void UpdateRates(decimal monthlyRate, decimal? dailyRate = null, string updatedBy = "System")
        {
            MonthlyRate = monthlyRate;
            DailyRate = dailyRate;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void UpdateAreaInfo(double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System")
        {
            AreaSqm = areaSqm;
            AreaNote = areaNote;
            Remarks = remarks;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        // Adds utility (electricity/water) applicability without touching the other flags — used by the bulk
        // import so a batch's utility choice also applies to RENEWED (reused expired/closed) stalls. Additive
        // by design: it never strips a fee a stall already carries (e.g. BaseRental/FishFee, or a utility set
        // earlier), so re-importing can't silently remove billing.
        public void AddUtilityFees(bool electricity, bool water, string updatedBy = "System")
        {
            if (electricity) Fees |= ApplicableFees.Electricity;
            if (water) Fees |= ApplicableFees.Water;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        public void UpdateDetails(string actualOccupant, string? nameOnContract, double? areaSqm, string? areaNote, string? remarks, string updatedBy = "System")
        {
            AreaSqm = areaSqm;
            AreaNote = areaNote;
            Remarks = remarks;
            
            var activeContract = Contracts.FirstOrDefault(c => c.IsActive);
            if (activeContract != null)
            {
                activeContract.UpdateOccupant(actualOccupant, nameOnContract, updatedBy);
            }
            
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void Close(DateOnly closedOn, string updatedBy = "System")
        {
            Status = StallStatus.Closed;
            ClosedAt = closedOn;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void Reopen(string updatedBy = "System")
        {
            Status = StallStatus.Active;
            ClosedAt = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public bool IsActive() => Status == StallStatus.Active;

        /// <summary>True when this NPM stall belongs to a per-LGU CUSTOM section (Section null + a custom name).</summary>
        public bool IsCustomSection => Section is null && !string.IsNullOrWhiteSpace(CustomSectionName);

        /// <summary>
        /// The daily stall fee to bill for this stall. A CUSTOM-section stall uses its own <see cref="DailyRate"/>
        /// (set at registration); every canonical NPM stall (and any stall without a positive custom rate) uses
        /// the tenant's ordinance daily rate, which the caller resolves as-of the collection date. This keeps
        /// Cantilan and all canonical sections on the ordinance rate exactly as before — only custom sections
        /// diverge — so billing, settlement, and report obligations stay in sync via a single rule.
        /// </summary>
        public decimal ResolveDailyFee(decimal ordinanceDailyRate)
            => IsCustomSection && DailyRate is { } r && r > 0m ? r : ordinanceDailyRate;

        /// <summary>
        /// True when this is an EXPIRED account: it has an active contract, but the term of every active
        /// contract has already lapsed (none still covers today), so it is no longer a current holder.
        /// A vacant stall (no active contract) or one still within term returns false. This is the single
        /// source of the stall-level "expired" rule — used by the closed-accounts register, the
        /// stall-holder roster, and the remove-inactive-stall guard so they can never diverge.
        /// </summary>
        public bool IsContractExpired()
        {
            var active = Contracts.Where(c => c.IsActive).ToList();
            return active.Count > 0 && active.All(c => c.IsExpired);
        }

        public void SetType(StallType type, string updatedBy = "System")
        {
            Type = type;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Sets this NPM stall's section: EITHER a canonical <see cref="MarketSection"/> OR a per-LGU custom
        /// section name, never both. A non-null <paramref name="section"/> clears any custom name; a custom
        /// name (with null section) clears <see cref="Section"/>. A custom section bills like Vegetable/Meat
        /// — flat daily fee, no fish.
        /// </summary>
        public void SetSection(MarketSection? section, string? customSectionName, string updatedBy = "System")
        {
            Section = section;
            CustomSectionName = section.HasValue || string.IsNullOrWhiteSpace(customSectionName)
                ? null : customSectionName.Trim();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }

}