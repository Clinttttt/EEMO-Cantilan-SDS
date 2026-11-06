# V18 — Madrid tenant reports, Revenue Source readiness and remittance investigation

Date: 2026-10-27 (Asia/Manila)
Scope: read-only investigation + tenant-display/reporting fixes; no production data writes.

## Evidence supplied
- Madrid Head Portal official monthly income visibly identifies **Municipality of Madrid**, yet lists zero-amount, hardcoded Cantilan-era template rent rows such as Tampak Commercial Center (TCC).
- Madrid Revenue Source list shows collection operations "Setup required" due to no tenant source/effective OR/CT policy, incomplete service settings and/or inactive collector. These are real readiness gates, not merely labels.
- The Carrascal Operations screenshot clearly displays "Carrascal Public Market CPM"; it is from a separate tenant screenshot and cannot establish a Madrid/Cantilan data leak.
- Remittance page displays ₱0 collector cash position with no eligible receipts. This alone does not establish that all LGU collected revenue is zero: only collector-owned canonical Collections count as remittable there. Office-origin posted Collections and legacy PaymentRecords are separate.

## Code investigation
1. **Confirmed hardcoded shared policy warning:** `ObligationWorkspace.razor` used "Cantilan's duration policy has not been confirmed for this municipality" when `DurationUnconfirmed` is true. That condition is tenant-dependent, so the text was misleading. Replaced with municipality-neutral "Policy confirmation required" without changing duration guards.
2. **Confirmed template-label defect:** `OfficialMonthlyIncomeStructure.Rows` contains static canonical Cantilan facility labels; `GetOfficialMonthlyIncomeQueryHandler` previously returned them regardless of the authenticated tenant. Report facts and revisions themselves were already derived through tenant-scoped stores. Fixed facility **display names** using the current tenant's `Facilities` records, and marks nondefault municipality's unconfigured zero-only rows as hidden *only on the printable statement*, while preserving every stable row key in the API and revenue-performance register. Rows with genuine cash or audited revisions remain visible. Preserved fixed row keys, classifications, financial facts, approved targets/adjustments, and grand totals. For retained historical cash without a registered current facility name, show a neutral code label rather than substituting Cantilan's specific name.
3. **Other financial report surfaces reviewed:** `GetFinancialReportQueryHandler`, `GetCollectionReportQueryHandler`, and `GetMonthEndReportQueryHandler` already retrieve tenant facility names and scope reporting by tenant. Static Cantilan label switches appear as fallbacks, not proof that foreign receipts were queried. Broader audit of every PDF/export remains to be done.
4. **Operation readiness is real:** `GetOperationSetupQueryHandler` checks tenant revenue classifications, effective receipt policy, collector assignments and actual Mobile capability statuses. Automatically claiming all operations Ready would permit undefined prices, incorrect official receipt types or inactive devices. Implementing approved onboarding defaults requires municipality-approved rules and source-specific evidence; no policies, rates, OR/CT custody or collector activation were invented.
5. **Remittance authority:** `RemittanceWorkflow` includes only canonical `Collections` with CollectorId for collector remittance; office-origin canonical cash is tracked separately and legacy money is outside canonical remittance. Updated UI to explain the empty scope and separately show the already-provided `AccountabilityPositionDto.OfficeOriginCollected` and `LegacyCollectionsOutsideRemittance` where supplied, without changing sums or allowing payment/remittance mutations. Legacy-only collectors are now enumerated from the authenticated tenant's collector list and disclosed individually using the existing legacy reader, while remaining ineligible for canonical remittance. Actual live Madrid data completeness is not verified.

## Verification
- Application Release build: succeeded, 7 pre-existing compiler/analyzer warnings, 0 errors.
- Disposable PostgreSQL `OfficialMonthlyIncomeTests`: 8 passed, 0 failed, including explicit other-municipality TCC name isolation, correct Madrid report label, hidden-on-print zero-only NCC/NPM/BBQ rows while retaining their stable register keys and stable financial totals.
- Focused Head Portal component suites (Remittance, Obligation Workspace, Monthly Collection Report, Official Report Panels): 152 passed, 0 failed.
- Two intermediate component failures were investigated; the final copy preserves the established policy wording and does not introduce account-form custody terminology in remittance. No assertion was weakened.
- PR #73 initially failed 2 full-CI integration regressions because filtering zero rows removed authoritative report catalog keys. The implementation was corrected to retain all keys for reconciliation, with a separate display-only visibility flag; both failing PostgreSQL scenarios passed in focused local reruns.
- Legacy-only collector: an added disposable PostgreSQL test verified a ₱2,300 historical payment remains visible separately with 0 canonical Collections and 0 remittable amount.
- `git diff --check` clean at the earlier checkpoint; rerun before promotion.
- Full CI, browser acceptance, authenticated Madrid data comparison, and protected deployment remain release gates.

## Operational restrictions
Do NOT auto-seed unapproved ordinances, source rates, receipt types, collector assignments or physical accountable form custody. Do NOT convert legacy cash into new canonical Collection/SRC or remittance. Do NOT treat a limited tenant archive as an independently restorable full backup. Do NOT claim that Madrid currently owes or collected zero based solely on a canonical-only remittance page.

## Follow-ups requiring separate investigation
- If the LGU has actual historical remittance records in older tables, they need an explicit read-only legacy register before any governed consolidation; adding them to canonical totals without provenance is unsafe.
- Formal tenant-aware labels for other historical fallback UIs/exports should be reviewed individually against effective facility names and archival requirements.
- Onboarding could generate a validated draft revenue-policy checklist from authoritative approved municipal inputs, but cannot silently invent missing legal fee or receipt rules.
