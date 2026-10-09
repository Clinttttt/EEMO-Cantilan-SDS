# Rental action content parity — 2026-10-09

## User-facing goal
Preserve the existing ICE/TCC/NCC/NPM/BBQ **Occupancy Registry** layout and five-icon action order, while making KM, Vegetable/Fruits Monthly and FA action contents source-appropriate, current and consistent. KM/VF/FA remain obligation sources, not Stall or Contract copies.

## Implemented
1. Extracted shared icon presentation in the preceding commit (`ba55aa0e`), with original stall handlers unchanged.
2. The **View Details** icon on KM/VF/FA now opens a separate, source-aware occupant/contract/space modal (instead of the mislabeled 12-month rental position): status, actual occupant, recorded basis, name and contract reference, dated term when present, area, rent and server-derived assessed/collected/remaining. No invented terms/dates. FA shows lot-holder and event facts without unsupported lease duration, contract or area fields. Layout and action placement follow the existing civic modal.
3. The **Payment History** icon still uses `ObligationRentalDetails` with authoritative obligation-period positions and canonical collection references. It preserves the documented limitation of activity-feed first allocations and does not claim an OR or account total from one collection line.
4. **View Profile** deep-links to the correct typed source account, with its existing office management and periods.
5. The **Edit** icon on active KM/VF now opens `Edit Space Holder` using the registration's existing `RentalLesseeFields`, `RentalContractFields`, `RentalEffectivityField` and space/area components. The space number and facility/operation are read-only, never moved to a different source. The amount command remains separate: `Change approved amount` is still effective-dated `SetRateAsync`.
6. Added Head-only `POST /api/obligations/accounts/{accountId}/holder` via typed client/DTO/service. It changes *only* descriptive source-holder and lease evidence, validates the existing domain rules, writes a tenant-scoped `AuditLog` with old/new JSON snapshots, and does not change rate, obligation-period or Collection/SRC entities. With assessed periods, occupant/lease basis/term/effective date are immutable; name change is also blocked for linked Payors. Closed and event accounts cannot use this editor.
7. Close remains the existing Head-confirmed `CloseAccountAsync`; it retains period and collection history. The fifth icon is visibly disabled for closed obligation accounts, with a genuine reason. It does **not** call the physical-stall toggle.

## Not silently implemented
**Reopening and renewal are not interchangeable.** The stall registry's status toggle cannot be reused for an obligation account: clearing `ActiveTo` could create previously closed monthly assessments. A safe successor-account workflow requires accepted office rules for whether reopening resumes the previous three-year term or starts a new term; prior-account linkage, tenant-scoped current-occupancy exclusivity and idempotent audit/migration must be designed and reviewed. FA event lots never automatically acquire a three-year lease. VF term remains to be individually confirmed.

**Full historical allocation parity** is also pending. Current activity feed supplies first source allocations only. Payment History correctly labels this limitation; do not present source-line amounts as settlement totals. The existing obligation period totals remain authoritative.

## Checks
Verified: full Web component suite **856 passed / 0 failed / 0 skipped** (`.tmp/space-actions-green-web.log`); full Unit suite **2526 passed / 0 failed / 0 skipped** (`.tmp/space-action-unit-full.log`); Client Release and API Release both **0 warnings / 0 errors** (`.tmp/space-action-client-release.log`, `.tmp/space-action-api-release.log`). BUnit additions verify occupant/contract modal content, the source-scoped edit request, history/profile routing and that opening forms cannot record money or close accounts. A targeted PostgreSQL integration regression for persisted holder evidence and audit snapshots compiled, but was **skipped** because the disposable PostgreSQL environment was unavailable. It is not a verified database pass. Live authenticated browser verification is also pending.

## Source protection
This work must not stage or overwrite unrelated dirty planning files:
`docs/planning/ACTIVE_WORKSTREAMS.md`, `docs/planning/CURRENT_RELEASE_STATE.md`, `docs/planning/VEGETABLE_FRUIT_WORKSPACE_20261008.md`.

Do not deploy/push/merge/migrate without acceptance. Authenticated browser review at ICE vs KM/VF/FA and mobile/narrow viewport remains required.
