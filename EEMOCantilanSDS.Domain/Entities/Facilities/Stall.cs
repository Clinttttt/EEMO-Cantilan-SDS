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
            Guid municipalityId = default)
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
    }

}