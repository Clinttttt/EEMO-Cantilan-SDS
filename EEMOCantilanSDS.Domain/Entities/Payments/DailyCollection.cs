using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
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
        // Legacy read-time figure: kilos x a rate that is NOT the rate in force on the collection date. It is kept only so
        // existing reports read exactly as before; the authoritative Weight & Measure amount is FishFeeAmountFrozen.
        public decimal? FishFeeAmount => FishKilos.HasValue 
            ? FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0;
        /// <summary>
        /// Frozen Fish weighing evidence (IA-049): the rate and its effective date resolved at collection time, and the
        /// amount they produced. All three are null on rows collected before this evidence existed - those stay
        /// unresolved and are never re-priced from today's rate - or where the office had stated no Fish rate.
        /// </summary>
        public decimal? FishFeeRatePerKilo { get; private set; }
        public DateOnly? FishFeeRateEffectiveDate { get; private set; }
        public decimal? FishFeeAmountFrozen { get; private set; }
        /// <summary>Optional NPM Meat weighing quantity recorded with this paid daily collection.</summary>
        public decimal? MeatKilos { get; private set; }
        /// <summary>The rate evidence resolved for this source event; null on rows without Meat weighing.</summary>
        public decimal? MeatFeeRatePerKilo { get; private set; }
        public DateOnly? MeatFeeRateEffectiveDate { get; private set; }
        /// <summary>Frozen NPM Meat weighing charge; persisted so report queries use the exact recorded amount.</summary>
        public decimal MeatFeeAmount { get; private set; }
        public decimal TotalCollected => IsPaid
                                          ? DailyFee + (FishFeeAmount ?? 0) + MeatFeeAmount
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
            string updatedBy = "System",
            decimal? meatKilos = null,
            decimal? meatFeeRatePerKilo = null,
            DateOnly? meatFeeRateEffectiveDate = null,
            decimal? fishFeeRatePerKilo = null,
            DateOnly? fishFeeRateEffectiveDate = null)
        {
            // Recollecting voided canonical rent must not replace a separate weighing event.
            if (SettlementAuthorityState == SettlementAuthority.Canonical && !IsPaid
                && (FishKilos is > 0m || MeatFeeAmount > 0m))
            {
                if ((fishKilos.HasValue && fishKilos != FishKilos)
                    || (meatKilos.HasValue && meatKilos != MeatKilos))
                    throw new InvalidOperationException("Existing weighing evidence cannot be replaced by a rent payment.");
                IsPaid = true;
                IsAbsent = false;
                return;
            }
            if (meatKilos is < 0m)
                throw new ArgumentOutOfRangeException(nameof(meatKilos));
            if (meatKilos.HasValue && (meatFeeRatePerKilo is not > 0m || meatFeeRateEffectiveDate is null))
                throw new ArgumentException("Meat kilos require a positive effective rate snapshot.", nameof(meatFeeRatePerKilo));
            if (meatFeeRateEffectiveDate > CollectionDate)
                throw new ArgumentException("Meat rate evidence cannot be effective after the collection date.", nameof(meatFeeRateEffectiveDate));
            if (!meatKilos.HasValue && (meatFeeRatePerKilo.HasValue || meatFeeRateEffectiveDate.HasValue))
                throw new ArgumentException("Meat rate evidence requires a Meat kilos source fact.", nameof(meatFeeRatePerKilo));
            // Fish weighing evidence: all-or-nothing, tied to recorded kilos, and never effective after the collection date.
            if (fishFeeRatePerKilo.HasValue != fishFeeRateEffectiveDate.HasValue)
                throw new ArgumentException("Fish rate evidence needs both the rate and its effective date.", nameof(fishFeeRatePerKilo));
            if (fishFeeRatePerKilo.HasValue && (fishFeeRatePerKilo <= 0m || fishKilos is not > 0m))
                throw new ArgumentException("Fish rate evidence requires positive Fish kilos and a positive rate.", nameof(fishFeeRatePerKilo));
            if (fishFeeRateEffectiveDate > CollectionDate)
                throw new ArgumentException("Fish rate evidence cannot be effective after the collection date.", nameof(fishFeeRateEffectiveDate));
            IsPaid = true;
            IsAbsent = false;
            ORNumber = orNumber;
            CollectorId = collectorId;
            FishKilos = fishKilos;
            FishFeeRatePerKilo = fishFeeRatePerKilo;
            FishFeeRateEffectiveDate = fishFeeRateEffectiveDate;
            FishFeeAmountFrozen = fishFeeRatePerKilo.HasValue ? fishKilos!.Value * fishFeeRatePerKilo.Value : null;
            MeatKilos = meatKilos;
            MeatFeeRatePerKilo = meatFeeRatePerKilo;
            MeatFeeRateEffectiveDate = meatFeeRateEffectiveDate;
            MeatFeeAmount = meatKilos.GetValueOrDefault() * meatFeeRatePerKilo.GetValueOrDefault();
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
            FishFeeRatePerKilo = null;
            FishFeeRateEffectiveDate = null;
            FishFeeAmountFrozen = null;
            MeatKilos = null;
            MeatFeeRatePerKilo = null;
            MeatFeeRateEffectiveDate = null;
            MeatFeeAmount = 0m;
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
            FishFeeRatePerKilo = null;
            FishFeeRateEffectiveDate = null;
            FishFeeAmountFrozen = null;
            MeatKilos = null;
            MeatFeeRatePerKilo = null;
            MeatFeeRateEffectiveDate = null;
            MeatFeeAmount = 0m;
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

        /// <summary>
        /// Who answers for this day's stall-fee money (IA-051/IA-062). Legacy: this row's own paid state is the money, as it
        /// always was. Canonical: the money is the posted Collection in <see cref="CanonicalCollectionId"/>; this row is then only
        /// the operational day (stall, date, attendance, weighing) and a projection of that Collection's state.
        /// </summary>
        public SettlementAuthority SettlementAuthorityState { get; private set; } = SettlementAuthority.Legacy;

        /// <summary>The canonical Collection that currently pays this day, when one does.</summary>
        public Guid? CanonicalCollectionId { get; private set; }

        /// <summary>A separately collected month-end adjustment; the original installment keeps its own authority.</summary>
        public Guid? CanonicalAdjustmentCollectionId { get; private set; }

        public void ApplyCanonicalAdjustment(Guid collectionId, DateTime? originalUpdatedAt, string? originalUpdatedBy)
        {
            if (collectionId == Guid.Empty || !IsPaid || MonthEndAdjustment is not > 0m)
                throw new InvalidOperationException("A paid installment and posted adjustment are required.");
            CanonicalAdjustmentCollectionId = collectionId;
            // The adjustment has its own Collection recognition date. Do not move the earlier rent or weighing.
            UpdatedAt = originalUpdatedAt;
            UpdatedBy = originalUpdatedBy;
        }

        public void ApplyCanonicalAdjustmentVoid()
        {
            if (CanonicalAdjustmentCollectionId is null) return;
            ClearMonthEndAdjustment();
            CanonicalAdjustmentCollectionId = null;
        }

        /// <summary>
        /// Hands this day's stall-fee money to a canonical Collection. Called only by the canonical posting path, for a day it
        /// has just marked paid; from then on the row's paid state follows the Collection (and its void), never a legacy edit.
        /// </summary>
        public void ApplyCanonicalPayment(Guid collectionId)
        {
            if (collectionId == Guid.Empty) throw new ArgumentException("A posted Collection is required.", nameof(collectionId));
            if (!IsPaid) throw new InvalidOperationException("Only a paid day can be handed to a canonical Collection.");
            SettlementAuthorityState = SettlementAuthority.Canonical;
            CanonicalCollectionId = collectionId;
            ORNumber = null;   // the canonical identity is the SRC; no typed serial is carried on this path
        }

        /// <summary>
        /// Projects the void of the canonical Collection that paid this day: the day is unpaid again. The row stays under
        /// canonical authority, so its earlier payment can never be counted from the legacy side, and it can be collected again.
        /// </summary>
        public void ApplyCanonicalVoid(string updatedBy = "System")
        {
            if (SettlementAuthorityState != SettlementAuthority.Canonical) return;
            // The Collection pays RENT only. Weighing remains independent legacy source evidence;
            // retain its collector and recognition timestamp as well as its frozen quantity/rate.
            IsPaid = false;
            IsAbsent = false;
            ClearMonthEndAdjustment();
            if (FishKilos is not > 0m && MeatFeeAmount == 0m)
            {
                CollectorId = null;
                UpdatedAt = DateTime.UtcNow;
                UpdatedBy = updatedBy;
            }
            CanonicalCollectionId = null;
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
            // A day paid by a canonical Collection is identified by its SRC and carries no typed serial.
            if (!IsPaid || SettlementAuthorityState == SettlementAuthority.Canonical) return;
            ORNumber = orNumber;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
