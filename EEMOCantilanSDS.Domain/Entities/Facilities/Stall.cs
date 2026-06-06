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
    public class Stall : AuditableEntity
    {
        public Guid FacilityId { get; private set; }
        public string StallNo { get; private set; } = string.Empty;
        public StallStatus Status { get; private set; } = StallStatus.Active;
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
            string createdBy = "System")
        {
            return new Stall
            {
                Id = Guid.NewGuid(),
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
        public void Close()
        {
            Status = StallStatus.Closed;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "System";
        }
        public void Reopen()
        {
            Status = StallStatus.Active;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "System";
        }
        public bool IsActive() => Status == StallStatus.Active;

        public void SetType(StallType type, string updatedBy = "System")
        {
            Type = type;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }

}