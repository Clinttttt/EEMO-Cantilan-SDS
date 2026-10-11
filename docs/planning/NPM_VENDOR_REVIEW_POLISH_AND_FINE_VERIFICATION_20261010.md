# NPM vendor review polish and Fine Assessment verification

October 10, 2026. Primary checkout `C:\dev\stalltrack\eemo`, branch `codex/vegetable-fruit-space-workspace`, starting HEAD `6f8585a4`. Implementation evidence; no new office policy or financial activation.

## Drawer correction

`NpmVendorRegistrationWorkflow` previously rendered technical evidence, correction, existing-link and new-registration controls together, with primary actions in the body and footer. It now separates candidate selection, occupant/source summary, registration status and an explicit action choice. No path is selected by default.

- Candidate rows emphasize actual occupant, area/stall and recorded occupancy/link status. Exact ContractId remains internal and readable through collapsed Source details after selection.
- The existing `ChoiceCards` presents Link existing registration / Register new vendor. Only the selected path's controls render. The fixed `VendorDrawerShell` footer owns its one primary action.
- Existing registration search remains manual; selected vendor/type/business/year and NPM area/stall are shown together before confirmation. A Verification note remains required.
- Exception and duplicate-review confirmations appear only in applicable contexts. A changed type in the existing `VendorRegistrationDrawer` exposes the source exception confirmation there. Server validation remains authoritative.
- Verified links, correction evidence and View registration remain accessible. Correction uses the existing endpoint, followed by authoritative reload and parent count refresh.
- Register new vendor reuses `VendorRegistrationDrawer` and the original atomic registration/association handler. Only the active drawer mounts; there is no hidden second primary action. No business/address is invented. Retry operation identity and exact contract/source version are unchanged.
- Head-only writes and Admin read-only review remain unchanged. Annual renewal, independent vendor registration/imports, Collections/SRCs, fees and rent are untouched.

## Fines capability status

Existing foundation `51d1493c`, settlement `28e15446`, association `43b05669` and migration correction `6f8585a4` are present. Fines was not rebuilt or reseeded.

| Capability | Status in this pass |
| --- | --- |
| Assess Fine | Implemented; local creation blocked by absent eligibility policies; authorized save verified in disposable PostgreSQL |
| Saved Assessments | Implemented; read/component verification passed; local register has no saved assessments |
| Account Review charges | Implemented for exact source account references; live queue null-ContractId gap is a separately requested follow-up, not resolved by this drawer patch |
| Standalone full fine OR | Implemented, PostgreSQL verified; authenticated acceptance pending; no actual local assessment to quote |
| Compatible rent + fine OR | Implemented for verified office account context, PostgreSQL verified; authenticated acceptance pending |
| Assigned Collector payment | Implemented standalone assignment enforcement, PostgreSQL verified; Collector mixed draft remains unsupported |
| Collected register / official Fines revenue | Implemented; legacy and correction-aware assessment cash tests passed; unpaid assessments remain excluded |
| Cancellation/reduction/waiver/refund | Not implemented; separate governed obligation/cash correction work |
| Partial fine payment / Mobile settlement | Not implemented |

## Local runtime and database evidence

Read-only checks used the configured development/user-secret connection, without printing credentials, with PostgreSQL `default_transaction_read_only=on` and `BEGIN READ ONLY` / `ROLLBACK`. Target: `localhost:5432 / EEMOCantilanSDS.DB`. This is actual local schema evidence, not inferred from disposable tests. Running-process environment overrides could not independently be inspected; the target is the resolved checkout configuration.

The inspected database records all of:

- `20261010013040_GovernedFineAssessments20261010`
- `20261010014548_FineAssessmentPolicyIntegrity20261010`
- `20261010021751_FineAssessmentCanonicalSettlement20261010`
- `20261010063119_FishMeatOptionalNpmAssociation20261010`

FineAssessments, FineAssessmentEvents, FineAssessmentPolicies and FishMeatNpmAssociations exist. Contract/obligation/policy tenant FKs and assessment source checks exist. Collection source-shape checks include source 15, and FineAssessment allocation identity check/FK exist. Counts: two penalty-definition records, zero assessment policies, zero assessments. Definitions alone do not authorize assessing an account. No fictitious policy was seeded; there is no local outstanding assessment for payment verification.

API PID 33148 serves checkout Debug binaries on 7097/5117, started at 15:31:12; API DLL was built at 15:31:10, after commit `6f8585a4` (15:29:20). Client PID 41252 serves 7167/5198, started at 15:31:27; its Debug DLL was built at 15:31:25, before this polish. API health and the unauthenticated local Fines route return HTTP 200; this is not proof of authenticated API success. Release binaries were rebuilt during validation; neither running process was restarted by this task.

Development configuration enables startup migrations. No API was launched/restarted, no tenant/hosted migration was applied, and no live financial record was created. Browser surface inventory was empty and the in-app browser was unavailable: authenticated Head/Collector/Fines acceptance remains pending. Role/source constraints are verified by code and disposable integration tests, not claimed as live login checks.

## Validation

- Focused Web NPM/Fine Assessment components: **31 passed / 0 failed / 0 skipped**.
- Full Web suite: **911 passed / 0 failed / 0 skipped**, including the concurrently corrected Audit Trail ARIA bindings.
- Full Unit suite, separately: **2,529 passed / 0 failed / 0 skipped**.
- Focused PostgreSQL FineAssessment plus NPM association: **54 passed / 0 failed / 0 skipped**. Real disposable PostgreSQL includes five migration compatibility shapes, creation/no-cash, role/tenant, standalone/mixed settlement, retry/race/correction/reporting and association tests.
- Final Client/API Release builds: each **0 errors / 0 warnings**. Earlier compilation/test logs contain existing nullable/analyzer warnings and the AngleSharp NU1902 advisory; no dependency change was made.
- All **167** source/generated scoped CSS files checked are brace-balanced. `git diff --check` passed.
- Six rendered component stages at 1440/1024/390px: **18** layout checks, zero document/control/footer overflow. Screenshots inspected for candidate, selected, link, duplicate-review, prefilled shared registration and long-name states. Fixed footer and collapsed GUID evidence are preserved. These are fixtures, not authenticated acceptance.

Commands: standard separate Release Unit/Web projects; Web focused filter `FullyQualifiedName~NpmVendorAssociationTests|FullyQualifiedName~FineAssessmentComponentTests`; PostgreSQL filter `FullyQualifiedName~FineAssessment|FullyQualifiedName~CollectionSessionTests.Npm_`; Client/API Release builds. Local artifacts/logs/screenshots are excluded from commits.

## Remaining activation and scoped follow-up

Smallest Fines activation step: verify real governing penalty evidence, then have the authorized Head record an applicable existing-definition/source eligibility policy in the existing setup workflow. No new implementation is needed to seed a demonstration. Authenticated creation/payment acceptance should use an explicitly authorized environment and actual permitted evidence; cash tests remain disposable.

The subsequently requested Account Review null-ContractId resolution and in-context Assess Fine transition are handled as a separate focused fix, preserving rental settlement targeting. Liability adjustments/refunds, partial payment and Mobile settlement remain deferred.

Task-owned files: the NPM workflow Razor/CSS, its component tests and this handoff. Protected ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE and VEGETABLE_FRUIT_WORKSPACE changes and concurrent Login/AuditTrail/Menu files are untouched and excluded. No branch/worktree change, push, merge, deployment or production action.
