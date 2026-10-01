# StallTrack V3 — Claude Cloud Continuation Handoff

## Purpose
This file transfers the state of the interrupted LOCAL Claude Code session into a new Claude Code Cloud session.
It is intentionally high-signal. Do not restart completed work. Verify with git history and code before editing.

## Repository state at handoff
- Repository: Clinttttt/EEMO-Cantilan-SDS
- Worktree used locally: C:\dev\stalltrack-v2-worktrees\claude-ui-v3
- Branch: interface-v3/claude-ui
- HEAD at handoff: e579c4e1
- Working tree: clean
- Branch is synchronized with origin/interface-v3/claude-ui
- No master merge, production deploy, production migration, backfill, APK publication, version bump or force-push has been done.

## Local Claude session evidence
The exact local Claude transcript exists only on the workstation at:
C:\Users\ASUS VIVOBOOK\.claude\projects\C--dev-stalltrack-v2-worktrees-claude-ui-v3\0f50e0f2-6c52-454c-ae21-714647674dc9.jsonl
Do NOT commit that raw transcript: it is ~67 MB and may contain unnecessary tool output or sensitive pasted material.
The final local assistant action before interruption was: "Writing the new Revenue Setup markup."
The interruption happened intentionally after the remittance slice was committed.

## Reference image now available to Cloud
Use this repo file for the official Monthly Income visual reference:
docs/reference/monthly-income-office-reference.png
## Latest completed commits
- e579c4e1 docs: add official monthly income layout reference
- 10c6c677 feat(remittance): record several collectors' remittances in one step
- 1c27ea95 feat(accountable-forms): batch assignment, audited transfer and an availability summary
- 1e7521d6 style(dashboard): make the overview hero one civic-blue surface
- 67c470ef style(mobile): replace native selects with one V3 choice control
- 79164a93 docs(wcf): record IA-054 direct Mobile entry and one-time enablement
- ba9b79cd feat(mobile): collect WCF against a prepared amount or a direct amount
- e3da82ac feat(wcf): show Collector Mobile status with one-step enablement on the Web
- 21677366 feat(wcf): collect a direct Water amount on Mobile with prospective authority
- 5df6b99e feat(wcf): add the one-time WCF Mobile enablement boundary
- 8dc2dc86 docs(mobile): document the V3 launch experience
- 8cb3674a style(mobile): align the native splash and host canvas with the launch screen
- c7d0971b style(mobile): replace the legacy dark bootstrap splash with the V3 launch
- 2786f66d refactor(mobile): decide launch status and route without an artificial wait
- eefd7135 docs(ui): define the Web Admin V3 visual system and report controls

## Current master task
Continue the release-candidate program titled:
"STALLTRACK V3 — WEB ADMIN POLISH, ACCOUNTABILITY BATCH WORKFLOWS, REPORT FIDELITY & BACKEND CLOSURE PASS"

Do not redo completed slices. Determine completion by inspecting commits and tests.

## Master-task progress at interruption
- Dashboard full-width civic-blue hero: DONE and committed (1e7521d6).
- Accountable Forms assignment UX + multi-collector batch + audited transfer: DONE and committed (1c27ea95).
- Multi-collector remittance convenience while preserving separate remittance records per collector: DONE and committed (10c6c677).
- Revenue Setup redesign: STARTED, NOT COMMITTED. Previous agent was inspecting V3 primitives and RevenueSetup.razor when interrupted.
## First unfinished task
Continue **Revenue Setup rework** first.
Target route: /settings/revenue
Relevant files include:
- EEMOCantilanSDS.Client/Components/Pages/Menus/RevenueSetup.razor
- EEMOCantilanSDS.Client/Components/Pages/Menus/RevenueSetup.razor.css
- shared V3 primitives in EEMOCantilanSDS.Client/wwwroot/app.css

Required direction:
- Use normal V3 workspace width; avoid narrow centered whitespace.
- Keep the revenue register as the main working surface.
- Replace the broken Add Revenue Source modal with a clean V3 right-side configuration drawer or equivalent focused surface.
- Manage must not expand configuration underneath the table; use a right-side drawer or dedicated focused page.
- Preserve effective-dated policy history. Historical policies remain immutable.
- Separate stable source identity from appended effective policy.
- Do not change monetary classifications or instruments merely for UI polish.

After Revenue Setup, continue the remaining unfinished sections of the master task in order:
1. Facility Configuration scope/alignment.
2. WCF "Prepare Water amounts" spacing/divider polish.
3. Official Monthly Income high-fidelity office report.
4. Financial Reports Collections excess-whitespace cleanup.
5. Backend / architecture gap closure and documentation drift.
6. Runtime/visual review and final validation.

## Official Monthly Income requirements
Route: /reports/monthly-income/official?year=2026
Visual reference: docs/reference/monthly-income-office-reference.png
Bagong Pilipinas logo:
EEMOCantilanSDS.Client/wwwroot/images/Bagong_Pilipinas_logo.png
Use the office report structure faithfully: municipal seal left, Bagong Pilipinas logo right, government heading, office heading, Jan–Dec, Annual Target, Total and Percentage.
Keep missing approved targets as "—"; do not invent values.
A4 landscape print must hide app chrome and avoid clipped Total/Percentage columns.
## Current canonical business rules — do not reinterpret

### Payor
Payor is the tenant-owned real-world financial identity and is independent of PayorUser/login.
Never auto-merge by display name, phone, OR/CT number or similar spelling.

### Accountable forms
Assignment/custody is NOT money.
Document issuance is NOT revenue by itself.
One serial can have only one valid custody/state at a time.
Consumed/issued documents never return to available stock.
Returned unused and spoiled blank forms are separately audited.
Cash remittance does NOT wait for a Cash Ticket range to be exhausted.

### OR / CT
One physical OR has one payer context but may contain multiple OR-compatible CollectionLines.
OR and CT never mix on the same physical accountable document.
Instrument is policy-driven, not collector-selected.

### WCF — IA-054 is current
- WCF = Cash Ticket.
- Direct amount only for current Cantilan workflow; no required meter/cubic-meter calculation.
- Head/Admin may optionally prepare an amount in advance.
- If none is prepared, authorized Collector Mobile may enter the direct WCF amount for an eligible source/payor.
- Office-prepared amount takes precedence.
- One-time operation activation enables prospective canonical WCF activity; do not require per-payor/per-month activation.
- Legacy historical Water rows remain legacy unless explicitly migrated.
- NPM is source context, not collector authorization.
- Cash Ticket custody/idempotency/reconciliation remain strict.

### ECF
ECF = Official Receipt.
Current Cantilan workflow is Direct Approved Amount; no required meter/kWh calculation.
## Other confirmed revenue rules
- Market Fees: CT, governed direct/fixed amount policy, Collector Mobile.
- Landing/Berthing: CT, day-to-day field collection, governed direct amount.
- Transportation/TRM: CT per transaction, vehicle-class approved effective rate, Mobile; historical TRM untouched.
- Transfer Large Cattle: OR, occasional direct approved amount, Mobile, simple payer/reference details.
- Vegetable/Fruit temporary space rental: Whole payment = OR; Daily transaction = CT; temporary/open-space context.
- Fish/Meat Vendor Fee: separate from NPM rent and Weight & Measure; OR; monthly-style obligation with flexible installments; prospective source only.
- Weight & Measure: OR; source tied to NPM Fish/Meat weighing; frozen evidence for new rows.
- Kanmanggay: Space Rental; OR; monthly per space; lightweight specialized space account.
- Fiesta/Araw: temporary lot/space rental; OR; Head/Admin approves event/lot/amount.
- Ice Plant: OR; monthly obligation mechanics but classification remains ICE_PLANT.
- Fines/Penalties: OR; dedicated PENALTIES_AND_FINES classification; approved definitions, not arbitrary free-text money.
- Slaughterhouse: OR; approved animal/service definitions. Packages/add-ons remain an explicit gap unless canonical definitions already exist.
- Tabo: OR from 2026-09-27; preserve older CT history.

## Reporting / authority rules
Source-authority logic must prevent double counting.
A mixed period may contain authoritative legacy pre-cutover + authoritative canonical post-cutover, each real collection exactly once.
Shadow/compatibility rows are comparison only and must not add money.
Collection, CollectionLine, document, remittance and report rows are distinct concepts.
Monthly Income cutover is prospective from approved production/source activation; no arbitrary historical backfill.

## Release safety
Never:
- merge to master;
- deploy production;
- apply production migration;
- perform production source cutover;
- backfill historical money;
- publish/sign APK;
- bump production version;
- force-push.
## Known remaining backend / product gaps to audit

### A. Legacy NPM Utility writer
EEMOCantilanSDS.Client/Components/Modals/UtilityBillModal.razor still contains legacy reading/rate concepts.
Current ECF/WCF workflow must not invite new current assessments through meter calculations.
Preserve historical reading evidence read-only where needed.
Do not create a second utility financial authority.

### B. Collection Activity
/collections/activity has historically been facility-only.
Final office activity should combine authoritative legacy + authoritative canonical collections exactly once using the established source-authority mechanism.
Do not blindly concatenate feeds.

### C. Mobile writers
Current docs previously identified no Mobile writer for:
- Fish/Meat Vendor Fee
- Kanmanggay
- Fiesta/Araw Lot Rental
Do not claim production readiness unless implemented and proven from confirmed rules.

### D. ECF Mobile
No ECF Mobile writer was previously present. Do not invent one if the intended release workflow is Web/Current Collection + OR; document intent accurately.

### E. Slaughterhouse
Approved animals exist. Packages/add-ons are not fully canonicalized unless later code proves otherwise.

### F. Annual targets
No approved target governance/source has been configured. Keep targets unavailable rather than inventing values.

### G. Integration skips
Seven integration tests have repeatedly been skipped.
Investigate and report each skipped test, reason, whether intentional, and release implication. Do not unskip blindly.

## Docs to reconcile
- docs/decisions/DECISION_REGISTRY.md
- docs/business/EEMO_OPERATIONAL_RULEBOOK.md
- docs/business/REVENUE_ARCHITECTURE.md
- docs/planning/MOBILE_V3_FUNCTIONAL_AUDIT_20260930.md
- docs/planning/MOBILE_V3_BACKEND_GAPS_20260930.md
Older WCF wording may be superseded by IA-054; preserve decision history but mark precedence clearly.
## Design direction
The target is a serious Philippine LGU / municipal enterprise system:
clean, restrained, civic, professional, data-dense where appropriate.
Use the V3 system: cool-neutral canvas, white work surfaces, civic blue, navy typography, restrained gold, thin borders, minimal shadows.
Avoid generic AI-dashboard styling, oversized decorative headers, gradients/glows, random colors, excessive cards and large dead whitespace.
For dense government registers, prefer strong tables over card explosions.
Use consistent drawers, buttons, fields, status badges, spacing and workspace headers.

## Validation expectations
Run focused tests per slice, then the appropriate full suite:
- Unit
- Component
- PostgreSQL integration
- Release solution/client build
- EF pending-model check if model touched
- git diff --check
- Mobile.Core / Android Debug / Android Release only if Mobile code changes

Perform runtime/visual review if the Cloud environment permits.
Do not claim visual verification if no browser/runtime inspection was actually performed.

## Final checkpoint
Report:
- branch / starting HEAD / final HEAD / clean status
- completed master-task sections
- Revenue Setup outcome
- Facility Configuration outcome
- WCF prepare-amount polish
- Official Monthly Income fidelity / print behavior
- Collections layout cleanup
- legacy utility writer resolution
- Collection Activity authority coverage
- remaining Mobile gaps
- skipped-test investigation
- exact test/build results
- commits
- production effects = none

## NEXT ACTION
Inspect git history and the current RevenueSetup.razor state, then continue the interrupted Revenue Setup redesign from the first unfinished point. Do not redo Dashboard, Accountable Forms or Remittance unless verification finds an actual defect.
