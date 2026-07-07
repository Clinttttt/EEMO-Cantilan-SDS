using System;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Onboarding
{
    /// <summary>
    /// A staged onboarding configuration for an approved LGU (Stage 2). Holds the LGU's draft config as an
    /// opaque JSON document until activation materializes it. Like <see cref="AssessmentRequest"/> this is a
    /// standalone, pre-LGU record — it is NOT tenant-owned and writes no live LGU data. The LGU edits it
    /// anonymously via a secure <see cref="Token"/> issued in the onboarding link at approval.
    /// </summary>
    public class OnboardingDraft : AuditableEntity
    {
        public Guid AssessmentRequestId { get; private set; }
        public string Municipality { get; private set; } = string.Empty;
        public string Province { get; private set; } = string.Empty;
        /// <summary>Unguessable capability token embedded in the onboarding link (the LGU's access credential).</summary>
        public string Token { get; private set; } = string.Empty;
        /// <summary>Opaque onboarding configuration document (stored as jsonb). Shape owned by the workspace UI.</summary>
        public string? ConfigJson { get; private set; }
        public bool IsSubmittedForValidation { get; private set; }
        public DateTime? SubmittedAt { get; private set; }
        public DateTime ExpiresAt { get; private set; }

        private OnboardingDraft() { }

        public static OnboardingDraft Create(Guid assessmentRequestId, string municipality, string province, string token, DateTime expiresAt)
        {
            var now = DateTime.UtcNow;
            return new OnboardingDraft
            {
                Id = Guid.NewGuid(),
                AssessmentRequestId = assessmentRequestId,
                Municipality = municipality.Trim(),
                Province = province.Trim(),
                Token = token,
                ExpiresAt = expiresAt,
                IsSubmittedForValidation = false,
                CreatedAt = now,
                CreatedBy = "Assessment Approval",
            };
        }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        /// <summary>Save the LGU's edited config. Editing after a submission re-opens the draft.</summary>
        public void UpdateConfig(string? configJson, string updatedBy)
        {
            ConfigJson = configJson;
            IsSubmittedForValidation = false;
            SubmittedAt = null;
            Touch(updatedBy);
        }

        public void SubmitForValidation(string updatedBy)
        {
            IsSubmittedForValidation = true;
            SubmittedAt = DateTime.UtcNow;
            Touch(updatedBy);
        }

        /// <summary>Re-open a submitted draft for corrections (operator returned it).</summary>
        public void Reopen(string updatedBy)
        {
            IsSubmittedForValidation = false;
            SubmittedAt = null;
            Touch(updatedBy);
        }

        private void Touch(string updatedBy)
        {
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
        }
    }
}
