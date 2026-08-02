using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    public class DailyCollection : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public Guid StallId { get; private set; }
        public Guid? CollectorId { get; private set; }
        public DateOnly CollectionDate { get; private set; }
        public decimal DailyFee { get; private set; } = FeeRates.NpmDailyFee;

        /// <summary>
        /// The month-end balance adjustment collected with this installment, when there is one: the part of the
        /// month's rent its calendar could not reach in ₱30 installments (see <see cref="AddMonthEndAdjustment"/>).
        /// Null for an ordinary day. Included in <see cref="DailyFee"/>, and kept separately so a receipt, a report
        /// or an audit can say what the extra was for.
        /// </summary>
        public decimal? MonthEndAdjustment { get; private set; }

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
            string createdBy = "System",
            decimal? dailyFee = null)
        {
            return new DailyCollection
            {
                Id = Guid.NewGuid(),
                StallId = stallId,
                CollectionDate = collectionDate,
                // Stamp the current municipality's resolved daily fee (falls back to the ordinance
                // constant, so Cantilan stamps the same ₱30 as before).
                DailyFee = dailyFee ?? FeeRates.NpmDailyFee,
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
            ClearMonthEndAdjustment();
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
            ClearMonthEndAdjustment();
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

        /// <summary>
        /// Takes back a month-end adjustment when this day stops being a collection. The adjustment is money the
        /// month was short, carried on this installment; a day that is no longer paid carries nothing, and leaving
        /// the inflated fee behind would count money the office never received.
        /// </summary>
        private void ClearMonthEndAdjustment()
        {
            if (MonthEndAdjustment is not { } carried) return;

            DailyFee -= carried;
            MonthEndAdjustment = null;
        }

        /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
        public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;

        /// <summary>
        /// Adds the month-end balance adjustment to this PAID installment.
        ///
        /// <para>A market space is let for a monthly rent and collected in daily installments, so a month whose
        /// calendar cannot reach the rent in installments — February's twenty-eight days at ₱30 fall ₱60 short of
        /// ₱900 — is short by the difference. Once the month has closed, that difference is collected with its last
        /// installment: the day's row carries it, so the month's ledger reaches the rent exactly and the shortfall
        /// is money received rather than an arrear that no day could ever clear.</para>
        ///
        /// <para>Kept as part of the installment (rather than a separate monthly record) because NPM money is
        /// day-by-day truth: every read sums these rows, so the adjustment is collected, receipted and audited
        /// exactly like the day it rides on. Only a paid day can carry one, and only ONE — a retried or repeated
        /// settlement must never charge the month twice.</para>
        /// </summary>
        public void AddMonthEndAdjustment(decimal amount, string updatedBy = "System")
        {
            if (!IsPaid || amount <= 0m) return;
            if (MonthEndAdjustment is not null) return;   // set once: the month is short by one difference, not many

            MonthEndAdjustment = amount;
            DailyFee += amount;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }

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
