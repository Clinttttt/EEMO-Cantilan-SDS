using EEMOCantilanSDS.Application.Dtos.Collections;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;
public enum MobileCorrectionReason { EnteredByMistake = 1, WrongPayerOrSource = 2, WrongAmount = 3, Duplicate = 4, Other = 5 }
public sealed record MobileCorrectionReasonIntent(MobileCorrectionReason Code, string? Note = null);
public sealed record RemoveMobileCollectionRequest(Guid ClientOperationId, Guid CollectionId, MobileCorrectionReasonIntent Reason);
public sealed record EditMobileCollectionIntent(Guid ClientOperationId, Guid CollectionId, MobileCorrectionReasonIntent Reason,
    CollectionSessionIntent Replacement);
public sealed record RecordMobileCollectionEditRequest(EditMobileCollectionIntent Intent, string QuoteFingerprint);
public sealed record MobileCollectionCorrectionResult(Guid CorrectionId, Guid OriginalCollectionId, string OriginalSRC,
    CollectionSessionCollection? Replacement, bool ExistingOutcome = false);
public sealed record MobileRecentCollection(CollectionActivityEventDto Collection, bool CanEdit, bool CanRemove, string? BlockReasonCode,
    string? BlockReason, SourceNativeActivityDto? SourceNativeContext = null, MobileCollectionEditSource? EditSource = null);
public sealed record MobileCollectionEditSource(CollectionSourceIdentity? SourceIdentity, CollectionSessionItemKind Kind,
    string OperationCode, CollectionSessionChoiceIdentity Identity, SourceNativeChargeIntent? Native = null,
    SessionSlaughterIntent? Slaughter = null, string? PayerSnapshot = null, string? Reference = null);
public sealed record MobileRecentCollections(DateOnly BusinessDate, IReadOnlyList<MobileRecentCollection> Rows, bool Truncated);
