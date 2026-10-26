# ST-21 investigation: one physical OR for a multi-month Space Rental checkout

Read-only findings that drove the implementation (commit 4e63424a and follow-ups).

## What the code did before

- `CollectionSessionWorkflow.RecordAsync` posted every session item through `sources.PostAsync(...)` with its own deterministic
  child operation id `ChildOperationId(tenant, session, item)`. There is no grouping: `CollectionSessionItemQuote.GroupId` equals that
  per-item id and `CollectionSessionInstrumentTotal` is a display total only. So N months = N Collections = N SRCs.
- A space month item is `SessionObligationIntent(AccountId, Year, Month)`; it posts through
  `CollectionComposerWorkflow.PostMobileObligationAsync`, one `CollectionLineDraft` with one `ObligationPeriod` allocation, and passes
  `document = null` to `CanonicalCollectionPostingCoordinator.PostAsync`. No OR is entered or consumed.
- Server guards per item: duplicate `Obligation|account|year|month` in one checkout is refused (`DuplicateBusinessEvent`); the amount may not exceed
  the period's outstanding balance; a partial amount is allowed. Idempotency: session fingerprint (`SessionIntentConflict`) plus the
  `PostingOperation` fingerprint of the child operation.

## Does the domain support one OR over several lines?

Yes, and by design for canonical sources:

- `Collection.Post` takes a list of `CollectionLineDraft`; each line has its own allocations. The coordinator calls
  `document?.Consume(collection.Id, clientOperationId, ...)` once, so one document belongs to exactly one Collection with many lines.
- Rulebook (IA-061): "One OR, several lines works through the existing composer for sources already on Canonical settlement authority".
  Space obligations are canonical.
- IA-062: a physical OR/CT never gates and is never required by a Collection; SRC identifies it. An OR is therefore optional evidence. The
  existing optional-OR pattern (NPM daily/whole/outstanding) is `NpmReceiptEvidence.ResolveAsync`: the OR must be a registered, unused document
  in the collector's custody (or Admin in-office), and instrument policy must be Official Receipt. This is reused unchanged.
- Instrument is fixed by classification policy, never chosen by the collector: space rental policy is Official Receipt (the quote already refuses
  anything else), so an OR is valid for these sources and a Cash Ticket is not.
- Mobile corrections: `MobileCollectionCorrectionWorkflow.ReverseAsync` reverses every line/allocation of a Collection (`c.Lines.Select(...)`), so a
  void of a multi-line collection works; edit-and-replace of a multi-line collection is blocked (`MultipleSourceBoundaries`), by design.
  Head `ReverseAsync` is line based as well. A reversal never frees the physical OR: the document stays Consumed (never reused).
- Reports (Collection Activity, Monthly Income, Financial Summary) read Collection lines; a multi-line Collection is one SRC row with per-line
  classification/period (same shape the NPM daily poster already produces).
- Offline queue: `PendingOperation.CollectionSession` stores the whole `RecordCollectionSessionRequest`, so a new optional intent field is
  persisted and replayed with `ClientOperationId` / `OwnerKey` unchanged. `EnqueueIssuedDocumentAsync` is a historical name; since IA-062 it
  only means durable local save before reporting captured. Mobile never validates an OR against a local book; the server does (same as NPM).

## Decision implemented

- New optional `CollectionSessionIntent.OfficialReceiptNumber` (null when absent, so old fingerprints and queued JSON are byte-identical).
- With an OR number: every item must be a month of the same space account (otherwise `ReceiptNeedsOneSpaceAccount`); the workflow posts ONE
  Collection via `CollectionComposerWorkflow.PostMobileObligationMonthsAsync` (a line per month, own amount, own period), consuming the OR once.
  Operation id is the child id of the lowest item id, so replay resolves to the same single SRC.
- Without an OR: unchanged. N months = N Collections / N SRCs, no document consumed.
- No schema change; no change to `CanonicalCollectionPostingCoordinator`.

## Not done / open

- No automatic spreading of one payment across months. Every month states its own amount (Mobile offers each at its full balance).
- Mobile does not know the OR book offline; an invalid OR is reported at Review (online) or on sync (rejection -> Needs review).
- Edit-and-replace of an OR multi-month Collection from Mobile stays blocked (void and re-record instead).
