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
        public ApplicableFees Fees { get; private set; }

        // NPM-specific
        public MarketSection? Section { get; private set; }

        // NCC-specific
        public NccAreaLocation? AreaLocation { get; private set; }

        // Physical info
        public double? AreaSqm { get; private set; }
        public string? AreaNote { get; private set; }

        // Rates
        public decimal MonthlyRate { get; private set; }
        public decimal? DailyRate { get; private set; }

        public Facility? Facility { get; private set; }
        public ICollection<Contract> Contracts { get; private set; } = new List<Contract>();
        public   ICollection<PaymentRecord> PaymentRecords { get; private set; } = new List<PaymentRecord>();
        public  ICollection<DailyCollection> DailyCollections { get; private set; } = new List<DailyCollection>();
        private Stall() { }
        public void UpdateRates(decimal monthlyRate, decimal? dailyRate = null, string updatedBy = "System")
        {
            MonthlyRate = monthlyRate;
            DailyRate = dailyRate;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void UpdateAreaInfo(double? areaSqm, string? areaNote, string updatedBy = "System")
        {
            AreaSqm = areaSqm;
            AreaNote = areaNote;
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

    }

}