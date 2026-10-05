# Itemized collection full-stack follow-up

Authority: explicit 2026-10-05 follow-up and Decision Registry IA-063/IA-064. This extends the existing `CollectionSessionWorkflow`; it does not introduce a financial engine or a visual system.

Release evidence: [Current release state](CURRENT_RELEASE_STATE.md) records the verified production API and signed
Collector 1.1.12 / 14. The earlier backend-only checkpoint is historical; the supported-source list below is current.

## Utilities

`UtilityBill.ElectricityDirectCollection` and `WaterDirectCollection` freeze direct mode without inventing an assessment. Existing bills default to prepared/legacy interpretation. Prepared mode retains authoritative remaining balance and partial allocation. ECF clean prospective authority is established with the first valid canonical posting; historical paid/partial/serial evidence remains protected. WCF still requires governed enablement and assignment. Rejected postings must not persist mode/cutover changes.

## Weighing

`WeighingCollectionSource` resolves existing linked NPM Fish/Meat occupancy, configured effective `IFeeRateResolver` rates, and OR policy. `NpmWeighing` identifies the canonical CollectionLine source; it is not another vendor registry or classification. Frozen calculation evidence carries kilograms, rate identity/effective date, policy and amount. Legacy DailyCollection weighing is preserved untouched.

## Mobile

`/collections/new`, `/ecf`, and `/weight-measure` use the existing Mobile shell and `CollectionSessionEditor`, with `MobileChoice` controls and existing bottom-sheet/button tokens. Server discovery controls the picker. Server quote controls displayed totals and record eligibility. Every returned Collection/SRC is shown; Records continues to read canonical child Collections.

Reviewed requests enter the existing ItemizedCollectionSession durable queue. Unquoted offline baskets require connection to review. Unknown responses retry the same intent. Review-only atomic rejections can be reconciled, corrected and explicitly requoted; recorded sessions and intent conflicts are not editable.

## Supported and deferred

Supported: WCF prepared/direct, ECF prepared/direct, Landing/Berthing, Market Fees with stable configured fee identity, Transportation, Transfer Large Cattle, Vegetable/Fruits approved modes, **direct additional Fish/Meat Vendor Fee** (IA-064), Weight & Measure, NPM Whole Payment, and Slaughterhouse's existing per-transaction writer. Vendor Fee uses the same direct workflow from standalone and basket; it creates no monthly period or allocation to the former account model.

Fish/Meat Vendor Fee remains selectable in New Collection. Its input is Amount received, never a monthly goal or
remaining obligation. Rent, Vendor Fee and weighing retain separate classifications, Collections/SRCs and remittance
eligibility. A PostgreSQL session regression posts 900 rent + 100 Vendor Fee + 66 weighing and verifies their independent
financial effects. A separate historical regression preserves pre-cutover obligation collections, SRCs and allocations
when a new direct fee is recorded.

Deferred: Tabo has no authoritative vendor-to-Business-Payor link for session ownership. NPM Daily requires the specialized day/cutover preflight rather than generic service amounts. Kanmanggay/Fiesta lack explicit safe Mobile assignment capability for this adapter. Their standalone workflows are preserved. Monthly facility rent is not generically adapted. NPM Whole Payment now reuses its existing settlement rules inside the session's outer Serializable transaction; it does not start a nested transaction.

Local capability consolidation from `b0db26e2`: [typed source handoff](ITEMIZED_SOURCE_CAPABILITY_HANDOFF.md).
The earlier Slaughterhouse support statement covered its quote/post adapter; discovery incorrectly looked for a
non-facility permission. This follow-up fixes discovery using its existing canonical facility menu entry and adds
typed single/multiple choice facts. It also fixes first-time WCF/ECF sharing a bill within one atomic session.
These are local backend changes, not evidence that a new production release has been deployed.

## Boundaries and deployment

One item retains one canonical Collection/SRC, including same-instrument items. Deterministic child operation identities, session fingerprint/replay and Serializable atomic posting remain. Only Collections/CollectionLines appear in reports and normal remittance; the session is not income. Additive migrations add two utility mode flags and permit weighing/direct Vendor Fee source shapes. Later explicit user authorization permits tested API deployment (including additive migrations) and signed Collector APK publication; the original no-deployment constraint is superseded for this release only.

## Local follow-up after publication

The Head Vendor Fee workspace now presents superseded monthly accounts as read-only historical evidence; it offers
Collection activity rather than opening, changing or collecting a monthly account. Its report labels the old position
historical while retaining canonical Monthly Income. Shared Kanmanggay/Fiesta workspaces keep their existing actions.
These additional Head presentation changes are local follow-up work, not part of the published `db75418b` image.
Claude's ongoing Mobile UI work was not changed by this follow-up.

Validation for this follow-up: 32 focused Head component tests (including Admin/SuperAdmin historical actions and the
unchanged space workflows), 7 Mobile component tests and 21 PostgreSQL session tests passed. Web Release built with
zero errors or warnings; `git diff --check` passed. The history action regression was observed failing before the UI
guard was added. No schema, rate, financial writer, Mobile styling or application version changed in this follow-up.
Interactive narrow-width/keyboard review remains unverified because no computer/browser surface was available.
