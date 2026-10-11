# Unified rental-registry action parity — 2026-10-09

## User acceptance target
The **existing** Occupancy Registry at `/vendors`, particularly the ICE/NCC action column, is the presentation reference. No new registry layout was requested. KM, Vegetable/Fruits Monthly and Fiesta/Araw are selectable sources using their existing typed obligation-account adapters, not new stall records.

## Implementation
- Extracted one `RentalRegistryActionButtons` component with the established five-icon order: **View Details / View Profile / Edit / Payment History / Close–Reopen**.
- Existing NPM/NCC/TCC/BBQ/ICE registry rows use the component with their **unchanged** `OpenDetail`, `ViewProfile`, `OpenEdit`, `OpenHistory`, `ToggleVendor` source handlers, including section-closure reopen restrictions.
- KM/VF/FA registry rows reuse that same component and styling, with their own `ObligationAccountDto` IDs and handlers. View Details opens the sourced 12-month rental-position dialog, View Profile opens the source's account-management deep link, and Payment History opens the canonical collection-reference drawer.
- On an active KM/VF account, Edit means **Change Approved Amount** using existing effective-dated `SetRateAsync` and confirmation UI—not unrestricted occupant/contract editing. FA event amount editing is unsupported and is visibly disabled. Only authorized Head users can access active-account rate/close actions; others see them disabled.
- Close calls the existing authorization-gated `CloseAccountAsync` confirmation, not a new financial writer. Closed accounts have a visibly disabled Reopen action because the current occupancy model lacks the audited successor-account implementation. No fake reopening/renewal occurred.
- On the shared table, space sources now show one payment-status pill instead of inline assessed/collected/remaining lines. The details drawer retains authoritative assessed, collected, and outstanding positions. Contract term/expiry is shown only when recorded signed date **and** duration both exist; no invented dates or duration. Optional source-recorded area location provides section/context; otherwise the operation name is displayed.

## Safety
No changes to `Stall`/contract source APIs, `ObligationAccount` persistence, posted Collections, immutable SRC identities, revenue classification, assessments, rates or historical correction logic. Payment positions are **not** receipts. No cross-source unified financial writer was added.

This is presentation parity, **not** a claim of financial or lifecycle capability parity. Full occupant edits, audited reopening, three-year renewal and event-specific lease governance remain separate scope gates.

## Verification
- Focused bUnit regressions cover all five actions for KM/VF/FA, typed navigation, existing effective-dated rate drawer, canonical-history surface, active/closed states and unavailable reopening, and preservation of original NCC registry actions.
- Final full Web component suite: **855 passed, 0 failed, 0 skipped** (`.tmp/registry-actions-web-verification.log`). Five additional active-profile/history/edit tests followed the original six cases; the source registry and all existing Web component tests remain green.
- Explicit Client Release build: **0 warnings, 0 errors** (`.tmp/registry-actions-client-release.log`). `git diff --check` passed.
- Actual authenticated runtime acceptance against the Cantilan tenant has **not** been performed. Compare `/vendors?facility=ice` with `/vendors?workspace=KM`, `/vendors?workspace=Vegetable%2FFruits` and `/vendors?workspace=FA`, including narrow viewport overflow.
- Three pre-existing unstaged planning-document edits must remain uncommitted and untouched by this ticket.

No database migration, production deployment, push, integration-branch merge, or APK publication is part of this follow-up.
