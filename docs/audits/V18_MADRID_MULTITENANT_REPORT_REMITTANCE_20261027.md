# V18 Madrid / Multi-Tenant Reporting & Remittance Audit — 2026-10-27

## Scope and evidence
User's authenticated Madrid Head Portal screenshots show (a) an official Monthly Income with Cantilan-specific NPM/NCC/TCC/BBQ headings; (b) a Revenue Setup list of eight "Setup required" operations; (c) a signed space contract duration warning mentioning Cantilan; (d) Remittance & Liquidation showing zero collected/remittable amounts. A further screenshot appears to be Carrascal's operation directory. All screenshots were interpreted as user-provided visual evidence; no production tenant database writes or credential reuse were attempted.

### Root causes
1. **Official income**: `OfficialMonthlyIncomeStructure.Rows` is a static historical Cantilan sheet. `GetOfficialMonthlyIncomeQueryHandler` used all those rows for every tenant even though canonical `Collection`, report revisions, and `LegacyMonthlyIncomeReader` are tenant scoped through the current EF context. This produces Cantilan names/phantom zeros inside other LGUs' official statements, not by itself proof of actual cross-tenant financial totals.
2. **Financial report**: `GetFinancialReportQueryHandler` and `GetCollectionReportQueryHandler` already resolve `IFacilityRepository.GetFacilityNamesAsync()` and only enumerate tenant codes; names from tenant facility records override code-based Cantilan fallbacks. Revenue Source Performance is derived from the official monthly statement, hence inherited the static-row problem. Other facility-specific pages and caches warrant follow-up coverage; do not claim whole system audited from these fixes.
3. **Remittance**: `RemittanceWorkflow` only issues/accountably covers actual posted canonical `Collection` rows with collector attribution, not legacy `PaymentRecord` rows or office-origin Collections. `BuildPositionAsync` previously enumerated collectors based only on canonical facts/form custody/remittances; legacy-only collectors vanished. The DTO already had `LegacyCollectionsOutsideRemittance` and `OfficeOriginCollected` but the Head Remittance screen concealed these. Office cash and legacy references cannot automatically become canonical remittances, since doing so could double count or fabricate accountable documents.
4. **Setup required**: `GetOperationSetupQueryHandler` explicitly evaluates the tenant's `RevenueClassifications`, effective receipt `RevenueClassificationPolicies`, `CollectorOperationAssignments` and current Mobile capabilities (service setup, online scope, active collectors, receipt custody). The missing readiness is authentic if these records are absent. Automatically assigning arbitrary OR/CT policy, rates, cash ticket custody or collector activation would impose Cantilan rules onto Madrid and is NOT an authorized fix.
5. **Contract duration**: `SignedContractDurationProblemAsync` intentionally refuses newly recorded signed Kanmanggay duration for nondefault LGUs lacking a confirmed local rule; its old refusal text used Cantilan by name, causing confusion. The gate stays in force, warning now names the current municipality neutrally.

## Implementation (isolated `fix/v18-madrid-tenant-report-readiness-20261027`)
- Nondefault official income uses same-tenant facility names and lists tenant-configured nonfacility sources and active stall facilities only (plus all rows with real financial cash or official audited revisions, even when retired). No post-hoc changes to cash/revenue totals. Default Cantilan report remains its historical full template.
- Reports avoid an empty second sheet for nondefault offices without B/C Terminal/Slaughterhouse groups.
- Remittance position now resolves tenant collectors even if they only have legacy collected cash, and retains read-only separately identified legacy balances in collector positions. Office-origin canonical receipts are explicitly disclosed; neither legacy nor office figures enter collector remittable totals or become new remittances. No remittance record or financial writer modified.
- Contract duration error text is neutral; no policy, contract or approval rules modified.

## Verified checkpoints
- Client Release build passed, 0 errors (104 pre-existing warnings).
- Focused bUnit OfficialReportPanels/RemittanceWorkspace tests initially 64/64 passed; after new UI regression, RemittanceWorkspace 25/25 passed, including excluded legacy vs office cash display.
- Initial disposable PostgreSQL suite: 29/30 passed; the one failure was an obsolete assertion requiring Cantilan static template rows in a synthetic nondefault LGU. It was updated to require only configured/paid sources for that LGU; subsequent 30/30 passed. The last run with the additional default-Cantilan compatibility test **31/31 passed, 0 skipped**.
- New PostgreSQL test confirms Madrid tenant NPM retains local name and excludes Cantilan Tampak row; separate Cantilan historical-template test passed in final rerun.
- New PostgreSQL test confirms legacy-only Madrid collector ₱2,300 is reported separately with 0 canonical amount and no eligible remittance rows; no double posting.
- No migrations, no production data writes, no tenant configuration changes.

## Remaining risks
- User must review/approve Madrid-specific operation source/rate/OR-CT/collector/device policies before an operation can safely be deemed collectible; "automatically work all" is not an authorized business rule.
- Real Madrid remittance numbers, current user session and full report navigation have not been inspected with authenticated production database read access; test proves isolated synthetic behavior, not real Madrid data.
- Legacy office-recorded payments without named collector still have no assignable collector position. Treasury turnover for office/legacy cash is a distinct design/audit problem; no retrospective remittance issued.
- No universal multi-tenant catalog sweep complete. Further pages with hardcoded Cantilan labels (e.g. code fallbacks, onboarding inventory) remain for later audit and may require separate fixes.
- Do not merge/deploy until final PostgreSQL/full CI plus security review pass and guarded release authorization.
