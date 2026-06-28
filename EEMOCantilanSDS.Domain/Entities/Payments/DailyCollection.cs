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

        // Excused/absent day: the payor was legitimately not operating (e.g. sick). It is NOT owed —
        // ₱0 due, no later payment — so financial recognition treats the day as non-collectable.
        // An absent record is always IsPaid=false (the two are mutually exclusive).
        public bool IsAbsent { get; private set; }

        public string? ORNumber { get; private set; }

        // Offline-sync idempotency key from the mobile client (null for online records). Lets a queued
        // offline collection be replayed safely on reconnect — a record with the same key is created once.
        public Guid? ClientOperationId { get; private set; }

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
            IsAbsent = false;
            ORNumber = orNumber;
            CollectorId = collectorId;
            FishKilos = fishKilos;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
        public void MarkUnpaid(string updatedBy = "System")
        {
            IsPaid = false;
            IsAbsent = false;
            ORNumber = null;
            CollectorId = null;
            FishKilos = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Marks the day as excused/absent: ₱0 owed, no collection, no fish, no OR. Clears any prior
        /// paid state. Phase 2 makes the financial layer treat this date as non-collectable.
        /// </summary>
        public void MarkAbsent(string updatedBy = "System")
        {
            IsAbsent = true;
            IsPaid = false;
            ORNumber = null;
            CollectorId = null;
            FishKilos = null;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
        public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;

        /// <summary>
        /// Stamps the OR (receipt) number on an already-PAID day — for when a collector recorded the
        /// collection in the field without an OR and an admin adds it later. Leaves the paid amount,
        /// collector, and fish kilos untouched. Only a paid day can carry an OR, so this is a no-op
        /// for an unpaid/absent record.
        /// </summary>
        public void SetOrNumber(string orNumber, string updatedBy = "System")
        {
            if (!IsPaid) return;
            ORNumber = orNumber;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
