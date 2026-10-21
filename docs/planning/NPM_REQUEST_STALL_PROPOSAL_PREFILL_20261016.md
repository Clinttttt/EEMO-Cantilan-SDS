# NPM Head request-to-form stall prefill — October 16, 2026

## Behavior
When the Head reviews a pending Fish/Meat-origin NPM registration and follows **Register NPM occupancy**, the link includes the **exact registered Fish/Meat vendor ID**, tax year, *proposed* stall and market section. The existing NPM → Registered Fish/Meat workflow reuses that identity. Only when the Head explicitly selects the exact matching vendor does the form prefill the proposed stall and section. These fields remain editable; contract, area, financial amounts and tenancy evidence are not filled or created from this request.

The proposed stall is **not** proof of vacancy, contract, payment, rate or tenancy, nor an allocation or approved NPM link. The Head must inspect recorded occupancies, resolve duplicates, confirm office evidence, and deliberately submit the separate NPM registration. The pending NPM request is approved later through its verified association, with no cash or SRC issued by approval.

## Context and UI
Fish/Meat fee registration and verified NPM occupancy are different records. The picker is titled **Select Fish / Meat vendor**; it shows **NPM occupancy not verified** until there is an actual association, and **Continue to NPM details** rather than a misleading Register action. NPM register offers the reused vendor identity compactly with verification controls.

The Head office register's latest committed UI uses shared tax-year/type selects, a compact filter row, a short note and an expandable **About this register** disclosure. Its existing **Edit details** action edits missing office-paper columns on the *same* vendor registration, guarded by an optimistic before-snapshot. These committed improvements are retained and are part of the next local Head-client refresh.

## Validation and scope
- Regression tests cover exact vendor/year/section matching, proposed stall remaining editable and not being overwritten by the next-stall suggestion, and preserving the previous approval safeguards.
- No database mutation, migration, new vendor, automatic tenancy, collection, SRC, OR, remittance, publication or APK change in this work.
- Worktree: `C:\dev\stalltrack-v2-worktrees\fish-meat-clean-review-20261016`, branch `fix/fish-meat-clean-review-20261016`.
- Main protected checkout `C:\dev\stalltrack\eemo`: eight pre-existing dirty files remain untouched. No automatic merge/push.
