using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Domain.Entities.Audit
{
    public class AuditLog : BaseEntity, IMunicipalityOwned
    {
        /// <inheritdoc />
        public Guid MunicipalityId { get; private set; }
        public string ActorId { get; private set; } = string.Empty;
        public string ActorName { get; private set; } = string.Empty;
        public string ActorRole { get; private set; } = string.Empty;   // "Admin", "Collector"
        public string Action { get; private set; } = string.Empty;   // "RecordPayment", "AddVendor"
        public string EntityType { get; private set; } = string.Empty;   // "PaymentRecord", "Stall"
        public Guid? EntityId { get; private set; }
        public string? OldValues { get; private set; }   // JSON snapshot before
        public string? NewValues { get; private set; }   // JSON snapshot after
        public string? IPAddress { get; private set; }
        public string? Notes { get; private set; }
        public DateTime LoggedAt { get; private set; }

        private AuditLog() { }

        public static AuditLog Create(
            string actorId,
            string actorName,
            string actorRole,
            string action,
            string entityType,
            Guid? entityId = null,
            string? oldValues = null,
            string? newValues = null,
            string? ipAddress = null,
            string? notes = null)
        {
            return new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = actorId,
                ActorName = actorName,
                ActorRole = actorRole,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OldValues = oldValues,
                NewValues = newValues,
                IPAddress = ipAddress,
                Notes = notes,
                LoggedAt = DateTime.UtcNow
            };
        }
    }
}
