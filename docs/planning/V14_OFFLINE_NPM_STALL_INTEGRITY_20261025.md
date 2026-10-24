# V14: offline checkout recovery, NPM shortcuts, deleted stall uniqueness (2026-10-25)

Working branch: feat/v14-revenue-integrity-20261024
Worktree: C:\dev\stalltrack\v14-revenue-next-20261024
Deployment: HOLD. No production API, Web, Mobile, database or migration deployment was performed.

## 1. Offline Collection Checkout
Root cause: a reconciliation-required itemized session is a durable PendingOperation. When opened using `/collections/new?sessionId=...`, the editor loaded its intent but had no display labels cached from the original entry, so persisted charges appeared as generic "Collection item". Removing a draft item did not change the durable pending operation, intentionally: it could be a posted/unknown outcome.
Change:
- The reopened editor shows typed, human-readable source labels from the stored charge intents when there is no live quote.
- Resolved rejection shows properly separated Review items, Refresh status, and Discard checkout buttons.
- Discard is explicit, confirmed twice and online-only. A fresh server GetCollectionSession must return 404 and the local queue entry must still satisfy CanReviewUnpostedSession (server-confirmed NeedsReview, no collections, no accountable document, no known SRC or session conflict). Otherwise the original is retained.
- When the user edits/removes a draft item, a notice clarifies the original queue entry remains until replacement posting or explicit discard. No local financial evidence is destroyed on mere draft edits.
- Existing replacement enqueue and idempotent replay are preserved; only the original rejected/unposted session is discarded after a successful new enqueue or explicit confirmed discard.

## 2. NPM occupant > Fish/Meat registration shortcut
Deep link with InitialContractId and a Head actor now opens VendorRegistrationDrawer immediately only for a current NPM occupancy with known type, no verified active link and no similar registrations requiring review.
- Existing linked registrations and similar-name matches retain the original review path; no duplicate association, identity guess, or conflicting link is automatically approved.
- Existing source-provenance note automatically carries the selected actual NPM contract/stall in the nested registration workflow.

## 3. NPM Add New Vendor > Registered Fish/Meat
Selecting a Fish/Meat registration now supplies its actual unique registration ID/tax year as source evidence without a second manual textbox.
- The explicit selection is treated as acknowledgement of the selected registered identity for the new occupancy form.
- Same-section Fish/Meat associations no longer show unnecessary verification checkboxes.
- A different area/type is a real exception and still requires explicit confirmation, per existing server rules; the UI does not falsely claim to have checked an exception.
- Existing strong backend association, duplicate, role, tenant, idempotency, and transactional checks are unchanged.

## 4. Closed/soft-deleted Stall 11 conflict
Verified mismatch: SoftDeleteStallCommandHandler sets IsDeleted, the global EF filter and form no longer include it, but PostgreSQL index IX_Stalls_FacilityId_Section_StallNo plus manually managed IX_Stalls_NullSection_Facility_CustomSection_StallNo previously included IsDeleted rows.
- New EF migration FilterDeletedStallsFromUniqueNumbers20261025 changes the canonical index and the manually managed custom/null-section index to enforce uniqueness only for NOT IsDeleted.
- Existing soft-deleted row remains in the database, including its stable identity and related receipts/contracts. A new row may take the same number in the same section without touching old balances.
- Two live rows in the same section remain forbidden at database level; other custom sections remain independent. No physical deletion or mutation of past collections.
- Do not deploy the new clients without this migration. Migration must be rehearsed and backed up before the user explicitly authorizes production schema change; this branch did not run it against production.
- Downgrade to old unique indexes will be blocked if new reuse created duplicates with soft-deleted history. Roll back application code while retaining the expanded unique constraints unless an explicitly reviewed data migration plan exists. Do not delete historical rows just to downgrade.

## Verification
- Focused Mobile component suite: 73 passed, 0 failed (includes rejected checkout local discard and server-posted negative case).
- Focused Web components: 140 passed, 0 failed (includes direct NPM registration and existing identity-review path).
- Isolated PostgreSQL migration + reuse tests: 2 passed, 0 failed, no skipped.
- Full unit, component and integration regression outcomes must be appended after completion; no physical Android offline/reconnect test or staging validation completed in this checkpoint.
- Known dependency advisory in component tests: AngleSharp 1.2.0 NU1902.
