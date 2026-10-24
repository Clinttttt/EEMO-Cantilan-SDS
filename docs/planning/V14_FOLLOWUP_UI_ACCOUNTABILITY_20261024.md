# V14 follow-up: report typography, rental numbers, receipt tracing, fine authority

Branch: feat/v14-revenue-integrity-20261024
Working tree: C:\dev\stalltrack\v14-revenue-next-20261024
Release decision: HOLD — no merge or production deploy.

## Scope
1. Monthly Collection Report: increase .me-table font moderately (12.5 -> 14px), header (11 -> 12px), monetary/monospace and status text slightly; keep responsive horizontal overflow and print styling.
2. Kanmanggay, Vegetable/Fruits and Fiesta/Araw rental tables: no secondary label beneath Space/Lot number. Fiesta/Araw event and event date are important, so display them in "Event / contract" rather than suppressing evidence; contract details and financial data are unchanged.
3. Accountability OR trace: official-accountable-documents trace has priority. The screenshot's receipt is a legacy Collection Activity payment and its recorded OR number does not establish an AccountableDocument. After official NotFound, inspect only tenant-scoped legacy activity for the selected month (API MaxDays 31, limit 5000), show matching legacy reference evidence without inventing SRC/remittance/physical OR issuance. Multiple matches remain separate; truncated or unavailable feeds carry explicit limitations. Tests cover positive, duplicate and absent legacy reference. The displayed screenshot alone cannot verify underlying production custody.
4. Direct fine: the DirectFineAuthority entity explicitly says NO normal application setup writer exists. Approved penalty definition and per-source fine assessment policy do not automatically authorize direct cash fines. To enable legitimately requires separate approved governance process: verified governing ordinance/authority reference, explicit no-registration collection approval, live approved penalty version, permitted amount/ceiling and dates, tenant SuperAdmin authorization, required payer/reason/reference, OR policy and accountable document issuance/reconciliation; immutable audit and conflict-safe activation/deactivation. No authorities were created or financial policies bypassed in this batch.
5. Fines page hero: strengthen the text contrast of the existing InstrumentChip only in the Fines hero.

## Other Codex worktree / merging
Other Codex session: C:\dev\stalltrack\eemo
Its reported clean branch: feat/weight-measure-multilgu-20261023 at 78d981a9.
Verified git merge-base of that branch and feat/v14-revenue-integrity-20261024 is 78d981a9: its current Weight & Measure commit is ALREADY an ancestor of this V14 branch. Do not merge or cherry-pick 78d981a9 again. If Codex adds newer commits, list and review only commits AFTER 78d981a9, resolve overlap deliberately, and run combined regression/release gates before approval. Do not modify either other worktree, reset, force-push, or deploy without authorization.

## Scope/limitations
No changed authoritative receipts, payment amounts, financial postings, tenants, governance records, schema or migrations. Production remains unmodified. Existing V14 Android signing/version, cross-vendor DB uniqueness, staging and real-device acceptance gates remain open.
