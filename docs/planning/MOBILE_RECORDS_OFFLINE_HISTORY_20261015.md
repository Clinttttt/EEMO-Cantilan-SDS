# Mobile Records and offline collection history — October 15, 2026

Independent worktree: `fix/mobile-records-offline-history-20261015` from `0fd23ac2`.

## Root causes
The legacy mobile facility-record DTO lacks a proven SRC reference and can have no OR number. The card previously showed billed amount first, not the amount collected. The device queue also retained successful itemized checkout intents (sometimes zero display amounts), causing duplicate generic SYNCED cards although canonical SRC collections exist in Records. The old label incorrectly counted all issued-state operations as physical Cash Tickets.

## Repair
Display collected amount first, with assessed amount beneath for partial payments. Show real OR evidence only when present; otherwise say receipt not linked. Show date/time and source context. Rename the review sheet Offline collection history. Suppress server-confirmed, reference-backed synced itemized sessions from this sheet **without deleting local records**, while preserving pending, failed, rejected, unconfirmed and physical-document evidence. Display exact session SRCs where known and avoid inventing a zero amount. No database, SRC issuance, financial rule, queue persistence or sync behavior changed.

## Verification
Mobile ComponentTests Debug and Release: 117 tests passed each; zero failures. Includes new offline history regression tests. Native Android build and on-device acceptance are separate; an installed phone app is not automatically updated by changing source files. Existing main-checkout edits and Codex work were untouched. No merge, push, deploy, or migration.

## Limitation
The legacy facility record cannot safely be assigned an SRC without a verified backend link. No payor/date/amount guessing was introduced.
