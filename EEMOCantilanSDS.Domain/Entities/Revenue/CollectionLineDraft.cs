using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Trusted domain input for constructing an immutable classified line. The caller must obtain the
/// classification and policy through its tenant-scoped persistence context.
/// </summary>
public sealed record CollectionLineDraft(
    RevenueClassification Classification,
    RevenueClassificationPolicy Policy,
    decimal Amount,
    CollectionSourceKind? SourceKind = null,
    Guid? SourceId = null,
    CollectionSourcePart? SourcePart = null);
