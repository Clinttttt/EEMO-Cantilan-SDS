using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Entities.Users;

/// <summary>Explicit authorization for one collector to work one non-facility operation.</summary>
public sealed class CollectorOperationAssignment : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectorId { get; private set; }
    public string OperationCode { get; private set; } = string.Empty;
    public DateTime AssignedAtUtc { get; private set; }
    public string AssignedBy { get; private set; } = string.Empty;

    public CollectorUser? Collector { get; private set; }

    private CollectorOperationAssignment() { }

    public static CollectorOperationAssignment Assign(
        Guid municipalityId, Guid collectorId, string operationCode, string assignedBy,
        DateTime? assignedAtUtc = null)
    {
        if (municipalityId == Guid.Empty || collectorId == Guid.Empty)
            throw new ArgumentException("A tenant and collector are required.");
        if (!CollectorOperationCodes.IsSupported(operationCode)
            || operationCode.Length > 64
            || operationCode.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')))
            throw new ArgumentException("The operation code must be a supported normalized operation identity.", nameof(operationCode));
        if (string.IsNullOrWhiteSpace(assignedBy) || assignedBy.Trim().Length > 100)
            throw new ArgumentException("The assigning actor is required.", nameof(assignedBy));

        var assignedAt = assignedAtUtc ?? DateTime.UtcNow;
        if (assignedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The assignment timestamp must be UTC.", nameof(assignedAtUtc));

        return new CollectorOperationAssignment
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            CollectorId = collectorId,
            OperationCode = operationCode,
            AssignedAtUtc = assignedAt,
            AssignedBy = assignedBy.Trim()
        };
    }
}
