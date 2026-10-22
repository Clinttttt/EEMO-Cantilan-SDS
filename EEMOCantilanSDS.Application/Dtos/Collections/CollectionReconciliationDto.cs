namespace EEMOCantilanSDS.Application.Dtos.Collections;

public enum CollectionReconciliationBasis { AsOriginallyKnown = 1, LatestCorrected = 2, CorrectionActivity = 3 }

/// <summary>Reconstructed canonical evidence, never an issued RCD snapshot or a remittance/deposit instruction.</summary>
public sealed record CollectionReconciliationDto(DateOnly From, DateOnly To, CollectionReconciliationBasis Basis,
    DateTime KnowledgeCutoffUtc, IReadOnlyList<CollectionActivityEventDto> Events, int MatchingCount, bool Truncated,
    decimal OriginalAmount, decimal CorrectionEffect, decimal NetAmount, string EvidenceNotice);
