using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// A bulk space-holder import that was actually saved: who ran it, when, for which space-rental line, and what it did. A preview, a failed
/// attempt or a cancelled upload leaves no run. It records the act of importing and changes no account, rate or Collection.
/// </summary>
public sealed class SpaceHolderImportRun : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public ObligationKind Kind { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public int RowCount { get; private set; }
    public int ImportedCount { get; private set; }
    public int SkippedCount { get; private set; }
    /// <summary>What was skipped and why (for example a space number that already has an account), kept short.</summary>
    public string? SkippedSummary { get; private set; }
    /// <summary>The submission that saved this import. Submitting the same list again with the same id answers with this run and creates nothing.</summary>
    public Guid? ClientOperationId { get; private set; }
    private SpaceHolderImportRun() { }

    public static SpaceHolderImportRun Record(Guid tenant, ObligationKind kind, Guid actorId, string actorName, int rows, int imported, int skipped, string? skippedSummary, Guid? clientOperationId = null)
    {
        if (tenant == Guid.Empty || actorId == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("InvalidImportRun");
        if (rows < 1 || imported < 0 || skipped < 0 || imported + skipped > rows) throw new ArgumentException("InvalidImportCounts");
        var summary = string.IsNullOrWhiteSpace(skippedSummary) ? null : skippedSummary.Trim();
        if (summary?.Length > 1000) summary = summary[..1000];
        var now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(), MunicipalityId = tenant, Kind = kind, ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Office User" : actorName.Trim()[..Math.Min(100, actorName.Trim().Length)],
            OccurredAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc),
            RowCount = rows, ImportedCount = imported, SkippedCount = skipped, SkippedSummary = summary,
            ClientOperationId = clientOperationId == Guid.Empty ? null : clientOperationId
        };
    }
}
