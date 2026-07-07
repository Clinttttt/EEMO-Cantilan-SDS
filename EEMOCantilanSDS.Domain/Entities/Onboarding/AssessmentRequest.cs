using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Onboarding
{
    /// <summary>Lifecycle status of a public LGU assessment request (platform-side, pre-LGU).</summary>
    public enum AssessmentRequestStatus
    {
        PendingReview = 1,
        Approved = 2,
        Declined = 3,
    }

    /// <summary>
    /// A public assessment request submitted by a prospective LGU (Stage 1 of onboarding). This is a
    /// standalone, pre-LGU record: it is <b>not</b> tenant-owned and touches no live LGU data — it is an
    /// inert review queue. Only Stage 4 (Activation) ever writes live, isolated LGU data. Mirrors the
    /// platform's assessment form; the operator reviews it and either approves (issuing an onboarding link)
    /// or declines.
    /// </summary>
    public class AssessmentRequest : AuditableEntity
    {
        public string Municipality { get; private set; } = string.Empty;
        public string Province { get; private set; } = string.Empty;
        public string RequestingOffice { get; private set; } = string.Empty;
        public string FocalPerson { get; private set; } = string.Empty;
        public string Position { get; private set; } = string.Empty;
        public string OfficialEmail { get; private set; } = string.Empty;
        public string ContactNumber { get; private set; } = string.Empty;
        /// <summary>Free-text summary of the facilities/revenue activities the LGU manages.</summary>
        public string FacilitiesManaged { get; private set; } = string.Empty;
        public string? ApproxVendors { get; private set; }
        public string? AuthorizationStatus { get; private set; }
        public bool Acknowledged { get; private set; }
        public string? Notes { get; private set; }

        public AssessmentRequestStatus Status { get; private set; }
        /// <summary>Presentational pipeline stage (Assessment → Onboarding → Validation → Activation).</summary>
        public string Stage { get; private set; } = "Assessment";
        public string? DecisionMessage { get; private set; }
        public string? OnboardingLink { get; private set; }
        public DateTime SubmittedAt { get; private set; }

        private AssessmentRequest() { }

        public static AssessmentRequest Create(
            string municipality,
            string province,
            string requestingOffice,
            string focalPerson,
            string position,
            string officialEmail,
            string contactNumber,
            string facilitiesManaged,
            string? approxVendors,
            string? authorizationStatus,
            bool acknowledged,
            string? notes)
        {
            var now = DateTime.UtcNow;
            return new AssessmentRequest
            {
                Id = Guid.NewGuid(),
                Municipality = municipality.Trim(),
                Province = province.Trim(),
                RequestingOffice = requestingOffice.Trim(),
                FocalPerson = focalPerson.Trim(),
                Position = position.Trim(),
                OfficialEmail = officialEmail.Trim(),
                ContactNumber = contactNumber.Trim(),
                FacilitiesManaged = facilitiesManaged.Trim(),
                ApproxVendors = string.IsNullOrWhiteSpace(approxVendors) ? null : approxVendors.Trim(),
                AuthorizationStatus = string.IsNullOrWhiteSpace(authorizationStatus) ? null : authorizationStatus.Trim(),
                Acknowledged = acknowledged,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                Status = AssessmentRequestStatus.PendingReview,
                Stage = "Assessment",
                SubmittedAt = now,
                CreatedAt = now,
                CreatedBy = "Public Submission",
            };
        }

        /// <summary>Approve the request and record the onboarding link issued to the LGU.</summary>
        public void Approve(string onboardingLink, string? decisionMessage, string updatedBy)
        {
            Status = AssessmentRequestStatus.Approved;
            Stage = "Onboarding";
            OnboardingLink = onboardingLink.Trim();
            DecisionMessage = string.IsNullOrWhiteSpace(decisionMessage) ? null : decisionMessage.Trim();
            Touch(updatedBy);
        }

        /// <summary>Decline the request with an optional notice message.</summary>
        public void Decline(string? decisionMessage, string updatedBy)
        {
            Status = AssessmentRequestStatus.Declined;
            DecisionMessage = string.IsNullOrWhiteSpace(decisionMessage) ? null : decisionMessage.Trim();
            Touch(updatedBy);
        }

        private void Touch(string updatedBy)
        {
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
