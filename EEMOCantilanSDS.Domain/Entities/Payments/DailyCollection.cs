using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    public class DailyCollection : AuditableEntity
    {
        public Guid StallId { get; private set; }
        public Guid? CollectorId { get; private set; }
        public DateOnly CollectionDate { get; private set; }
        public decimal DailyFee { get; private set; } = FeeRates.NpmDailyFee;
        public bool IsPaid { get; private set; }
        public string? ORNumber { get; private set; }

        public decimal? FishKilos { get; private set; }
        public decimal? FishFeeAmount => FishKilos.HasValue 
            ? FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0;
        public decimal TotalCollected => IsPaid
                                          ? DailyFee + (FishFeeAmount ?? 0)
                                          : 0;
        public Facilities.Stall? Stall { get; private set; }
        private DailyCollection() { }

        public static DailyCollection Create(
            Guid stallId,
            DateOnly collectionDate,
            string createdBy = "System")
        {
            return new DailyCollection
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                CollectionDate = collectionDate,
                DailyFee = FeeRates.NpmDailyFee,
                IsPaid = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        public void MarkPaid(
            string orNumber,
            Guid? collectorId,
            decimal? fishKilos = null,
            string updatedBy = "System")
        {
            IsPaid = true;
            ORNumber = orNumber;
            CollectorId = collectorId;
            FishKilos = fishKilos;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void MarkUnpaid(string updatedBy = "System")
        {
            IsPaid = false;
            ORNumber = null;
            CollectorId = null;
            FishKilos = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
