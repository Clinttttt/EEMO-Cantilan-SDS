using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Domain.Entities.Users
{
    /// <summary>
    /// A single-use, expirable code issued to a stall payor so they can self-activate an online
    /// account. Activation requires BOTH the code and the payor's registered contact number to match
    /// (the contact number is captured here when the code is issued). A contact number maps to exactly
    /// one payor account: a code may only be issued for a number that is not already registered or
    /// pending on another stall, so activation never merges two different occupants.
    /// </summary>
    public class PayorActivationCode : AuditableEntity
    {
        public string Code { get; private set; } = string.Empty;
        public string ContactNumber { get; private set; } = string.Empty;
        public Guid StallId { get; private set; }
        public DateTime ExpiresAt { get; private set; }
        public bool IsUsed { get; private set; }
        public DateTime? UsedAt { get; private set; }
        public Guid? PayorUserId { get; private set; }

        public Stall? Stall { get; private set; }

        private PayorActivationCode() { }

        public static PayorActivationCode Create(
            string code,
            string contactNumber,
            Guid stallId,
            DateTime expiresAt,
            string createdBy = "System")
        {
            return new PayorActivationCode
            {
                Id = Guid.NewGuid(),
                Code = code,
                ContactNumber = contactNumber,
                StallId = stallId,
                ExpiresAt = expiresAt,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }

        /// <summary>True when the code is unused, unexpired, and the supplied contact number matches.</summary>
        public bool CanBeRedeemedBy(string contactNumber) =>
            !IsUsed
            && ExpiresAt > DateTime.UtcNow
            && string.Equals(ContactNumber, contactNumber, StringComparison.Ordinal);

        public void MarkUsed(Guid payorUserId)
        {
            IsUsed = true;
            UsedAt = DateTime.UtcNow;
            PayorUserId = payorUserId;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = "Self-Activation";
        }

        // Unambiguous alphabet (no I/O/0/1) for codes read aloud / handwritten in the field.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Generates a single-use, human-friendly code in the form XXXX-XXXX.</summary>
        public static string GenerateCode()
        {
            Span<byte> bytes = stackalloc byte[8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

            var chars = new char[9];
            var pos = 0;
            for (var i = 0; i < 8; i++)
            {
                if (i == 4) chars[pos++] = '-';
                chars[pos++] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
            }
            return new string(chars);
        }
    }
}
