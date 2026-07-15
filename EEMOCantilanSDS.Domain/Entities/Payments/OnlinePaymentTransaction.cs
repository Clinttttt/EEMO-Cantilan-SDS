using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Payments
{
    /// <summary>
    /// An online payment attempt against a single <see cref="PaymentRecord"/>. Tracks the gateway
    /// hand-off (checkout session) and the received payment, then carries through to staff OR encoding.
    /// The OR number is NEVER auto-generated here — it is encoded by staff via <see cref="CompleteWithOr"/>.
    /// </summary>
    public class OnlinePaymentTransaction : AuditableEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public string Reference { get; private set; } = string.Empty;     // internal, e.g. EEMO-OP-20260613-AB12CD34
        public Guid PayorUserId { get; private set; }

        // What this payment settles. Monthly-rental facilities link a single PaymentRecord; NPM (daily)
        // has no monthly record, so it targets a whole month of that stall's daily ₱30 fees instead.
        public OnlinePaymentTargetKind TargetKind { get; private set; } = OnlinePaymentTargetKind.MonthlyRecord;

        // Set for MonthlyRecord targets (null for NPM daily-month).
        public Guid? PaymentRecordId { get; private set; }

        // Set for NpmDailyMonth targets (null for monthly): the NPM stall + billing month whose unpaid
        // daily fees this payment settles.
        public Guid? TargetStallId { get; private set; }
        public int? TargetYear { get; private set; }
        public int? TargetMonth { get; private set; }

        public decimal Amount { get; private set; }
        public OnlinePaymentStatus Status { get; private set; } = OnlinePaymentStatus.Initiated;
        public string Provider { get; private set; } = string.Empty;      // "PayMongo"
        public string? GatewayReference { get; private set; }             // checkout session id (dedupe key)
        public string? CheckoutUrl { get; private set; }                  // hosted checkout URL (for resuming an unfinished payment)
        public string? PaymentId { get; private set; }                    // provider payment id (pay_...)
        public string? Method { get; private set; }                       // "gcash"
        public DateTime? PaidAt { get; private set; }
        public string? RawPayload { get; private set; }                   // last gateway payload (jsonb, audit)
        public string? ORNumber { get; private set; }                     // staff-encoded later

        public PaymentRecord? PaymentRecord { get; private set; }

        private OnlinePaymentTransaction() { }

        public static OnlinePaymentTransaction Create(
            string reference,
            Guid payorUserId,
            Guid paymentRecordId,
            decimal amount,
            string provider,
            string createdBy = "Online")
        {
            return new OnlinePaymentTransaction
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                PayorUserId = payorUserId,
                TargetKind = OnlinePaymentTargetKind.MonthlyRecord,
                PaymentRecordId = paymentRecordId,
                Amount = amount,
                Provider = provider,
                Status = OnlinePaymentStatus.Initiated,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>
        /// Creates a transaction that settles a whole month of an NPM stall's daily ₱30 fees (base fee
        /// only — fish ₱/kg is weighed at the stall and utilities are billed separately). It carries the
        /// stall + billing month instead of a monthly PaymentRecord; settlement marks that month's unpaid
        /// contract-covered days paid (blank OR) and staff encode the OR afterward.
        /// </summary>
        public static OnlinePaymentTransaction CreateForNpmMonth(
            string reference,
            Guid payorUserId,
            Guid stallId,
            int year,
            int month,
            decimal amount,
            string provider,
            string createdBy = "Online")
        {
            return new OnlinePaymentTransaction
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                PayorUserId = payorUserId,
                TargetKind = OnlinePaymentTargetKind.NpmDailyMonth,
                TargetStallId = stallId,
                TargetYear = year,
                TargetMonth = month,
                Amount = amount,
                Provider = provider,
                Status = OnlinePaymentStatus.Initiated,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>True for end states that must not be mutated further by webhooks.</summary>
        public bool IsTerminal => Status is OnlinePaymentStatus.Completed
            or OnlinePaymentStatus.Failed
            or OnlinePaymentStatus.Cancelled
            or OnlinePaymentStatus.Expired;

        /// <summary>True once money has been received (Paid or OR-completed) — used for idempotent webhook handling.</summary>
        public bool IsSettled => Status is OnlinePaymentStatus.Paid or OnlinePaymentStatus.Completed;

        /// <summary>Records the gateway hand-off (checkout session created) and moves to Pending.</summary>
        public void SetPending(string gatewayReference, string checkoutUrl)
        {
            if (Status != OnlinePaymentStatus.Initiated)
                throw new InvalidOperationException($"Cannot move to Pending from {Status}.");

            GatewayReference = gatewayReference;
            CheckoutUrl = checkoutUrl;
            Status = OnlinePaymentStatus.Pending;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "Online";
        }

        /// <summary>True while the payor can still be sent back to an unfinished hosted checkout.</summary>
        public bool IsResumable =>
            Status is OnlinePaymentStatus.Pending or OnlinePaymentStatus.Initiated
            && !string.IsNullOrWhiteSpace(CheckoutUrl);

        /// <summary>
        /// Records the payment as received. Caller guarantees the amount matched and the event is not a
        /// duplicate. A confirmed payment SUPERSEDES a prior NON-settled terminal state (Failed / Cancelled /
        /// Expired): webhook delivery is not ordered and a payor may succeed after an earlier failed attempt
        /// or a late expiry, so money actually received must always be recorded. Only an already-settled
        /// transaction (Paid / Completed) is left untouched (idempotent no-op).
        /// </summary>
        public void MarkPaid(string? paymentId, string? method, DateTime paidAt, string rawPayload)
        {
            if (IsSettled) return;                       // Paid / Completed → idempotent no-op

            PaymentId = paymentId;
            Method = method;
            PaidAt = paidAt;
            RawPayload = rawPayload;
            Status = OnlinePaymentStatus.Paid;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "Online";
        }

        public void MarkFailed(string rawPayload) => SetTerminal(OnlinePaymentStatus.Failed, rawPayload);
        public void MarkCancelled(string rawPayload) => SetTerminal(OnlinePaymentStatus.Cancelled, rawPayload);
        public void MarkExpired(string rawPayload) => SetTerminal(OnlinePaymentStatus.Expired, rawPayload);

        private void SetTerminal(OnlinePaymentStatus status, string rawPayload)
        {
            // Never override a successful payment with a later failure/expiry event.
            if (IsSettled || IsTerminal) return;

            RawPayload = rawPayload;
            Status = status;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "Online";
        }

        /// <summary>Attaches the staff-encoded Official Receipt and completes the transaction.</summary>
        public void CompleteWithOr(string orNumber, string updatedBy)
        {
            if (Status != OnlinePaymentStatus.Paid)
                throw new InvalidOperationException($"Only a Paid transaction can be completed with an OR (was {Status}).");

            ORNumber = orNumber;
            Status = OnlinePaymentStatus.Completed;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
