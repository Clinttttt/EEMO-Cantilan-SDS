using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum OfficialReportRevisionKind { AnnualTarget = 1, MonthlyAdjustment = 2 }

/// <summary>Append-only report governance evidence. Never a Collection, obligation or remittance.</summary>
public sealed class OfficialReportRevision : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public string IntentFingerprint { get; private set; } = "";
    public OfficialReportRevisionKind Kind { get; private set; }
    public string RowKey { get; private set; } = "";
    public int Year { get; private set; }
    public int Month { get; private set; }
    public int Revision { get; private set; }
    public Guid? SupersedesId { get; private set; }
    /// <summary>Approved annual target or signed adjustment delta, according to Kind.</summary>
    public decimal Amount { get; private set; }
    public decimal? SystemAmountAtRevision { get; private set; }
    public string SourceOrReason { get; private set; } = "";
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    private OfficialReportRevision() { }

    public static OfficialReportRevision Record(Guid tenantId, Guid operationId, string fingerprint,
        OfficialReportRevisionKind kind, string rowKey, int year, int month, int revision, Guid? supersedesId,
        decimal amount, decimal? systemAmount, string sourceOrReason, string? reference, string? note,
        Guid actorId, string actorName, DateTime recordedAtUtc)
    {
        if (tenantId == Guid.Empty || operationId == Guid.Empty || actorId == Guid.Empty || revision < 1)
            throw new ArgumentException("Tenant, intent and actor identities are required.");
        if (!Enum.IsDefined(kind) || year is < 2000 or > 2200 ||
            (kind == OfficialReportRevisionKind.AnnualTarget ? month != 0 || amount < 0m : month is < 1 or > 12 || systemAmount is null))
            throw new ArgumentException("Invalid report revision scope.");
        if (decimal.Round(amount, 2) != amount || string.IsNullOrWhiteSpace(rowKey) || rowKey.Length > 100 ||
            amount is > 9999999999999999.99m or < -9999999999999999.99m ||
            string.IsNullOrWhiteSpace(actorName) || actorName.Length > 100 || fingerprint.Length != 64 ||
            string.IsNullOrWhiteSpace(sourceOrReason) || sourceOrReason.Trim().Length > 1000 ||
            reference?.Length > 200 || note?.Length > 1000 || recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Supply valid money precision, source/reason and report metadata.");
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenantId, ClientOperationId = operationId,
            IntentFingerprint = fingerprint, Kind = kind, RowKey = rowKey, Year = year, Month = month,
            Revision = revision, SupersedesId = supersedesId, Amount = amount, SystemAmountAtRevision = systemAmount,
            SourceOrReason = sourceOrReason.Trim(), Reference = reference?.Trim(), Note = note?.Trim(),
            ActorId = actorId, ActorName = actorName, RecordedAtUtc = recordedAtUtc };
    }
}
