# StallTrack Current Release State

**Status:** Production release verified; newer local integration checkpoint accepted for continued development
**As of:** 2026-10-07
**Production code basis:** `master` at `db75418b68aa221cc5cd30a3bfa2af9799c2a5c6` (2026-10-05 release)
**Current local integration checkout:** `C:\dev\stalltrack\eemo`
**Current local integration branch / HEAD:** `integration/report-governance-ui` at `6d3362f9`
**Collector APK in production:** `collector-1.1.12-14` (display version 1.1.12, version code 14)

This is the single current implementation-status entry point. It deliberately separates **deployed production** from the **newer accepted local integration state**. Dated audits and handoffs remain evidence; they do not become production merely because their commits are integrated locally. Before changing code, confirm the current Git HEAD because the local integration checkpoint may advance.

## 2026-10-07 accepted local integration checkpoint

The local integration branch now contains the October 6 office-clarification implementation and the October 7 collector/productivity follow-ups. These changes are **not pushed, not merged to master, not deployed, and not published as an APK** at this checkpoint.

Integrated local behavior includes:

- complete A/B/C Official Monthly Income structure support, with **Income From Terminal** separate from Transportation/Parking and Slaughterhouse standalone;
- Transportation/Parking as CT direct amount, with Terminal owning its own section/vehicle-assisted workflow;
- independent Fish/Meat vendor registration, Active/Closed lifecycle, ID-based annual renewal, management totals, registry import, and Weight & Measure requiring the registered vendor source;
- source-native New Collection behavior without a required Business Payor master;
- NPM Daily and Whole Payment discovery, reviewed Daily Collect All with one Collection/SRC per selected stall, and truthful blocked-readiness messaging;
- current-collector recent collections plus audited Edit/Remove correction; Mobile Edit is atomic through the real API/DI path and creates a replacement Collection/SRC rather than mutating posted money;
- Terminal optional payer/name snapshot without identity creation;
- Revenue Source Performance aligned to the static five-column management view: **Source · Collected · Annual target · Contribution · Attention**;
- Web/Mobile V3 presentation follow-ups for Fish/Meat, Collection Activity, Market Fees, recent registers, NPM Whole Payment, and related responsive states.

Additive local migrations now present in the accepted integration history include:

- `20261006173902_OfficeSourceNativeIdentity`
- `20261007000912_NativeCollectionSourceShapeAndSpaceHolder`
- `20261007072024_FishMeatRegistryLifecycle`

They have been exercised against throwaway/local PostgreSQL during development. **They have not been applied to production by this local integration checkpoint.** A local development database that has not applied them may fail with missing-table errors such as `FishMeatVendorRegistrations`.

Latest implementation handoffs:

- `COLLECTOR_PRODUCTIVITY_FOLLOWUP_HANDOFF_20261007.md`
- `FISH_MEAT_REGISTRY_MANAGEMENT_HANDOFF_20261007.md`
- `MOBILE_EDIT_ATOMICITY_FOLLOWUP_HANDOFF_20261007.md`
- `NPM_ARREARS_READINESS_HANDOFF_20261007.md`

Validation evidence across those accepted follow-ups includes a full Unit pass of **2,519**, a full Integration pass of **362 passed / 7 snapshot-gated skipped / 0 failed** at the Mobile Edit atomicity checkpoint, plus later focused NPM/readiness and Mobile component passes after the final UI/readiness refinements. Later focused passes did not rerun every full suite; read each handoff for exact scope.

Current local NPM note: the tested Cantilan `personal1` collector had five unpaid/pending stalls but `CanonicalCollection=false`; batch readiness correctly returned `SourceStillLegacy`. Collect All therefore remains unavailable until the approved NPM canonical switch is enabled. The UI must not describe that state as “nothing left to collect.”

## 2026-10-06 office clarification — production/runtime divergence

Direct MEEDO office clarification on 2026-10-06 changed several target business rules after the published 2026-10-05 release.

**The 2026-10-06 clarification remains business authority. The accepted local integration checkpoint now implements most of this direction, while the currently deployed production release may still show the older behavior until an explicit production rollout occurs.**

Confirmed changes:

- the Official Monthly Income continues with **B. Income From Terminal** (Comfort Room; Pull Pul Vans, Cargo Vans; Tricycad), **C. Income from Slaughterhouse**, and **OVERALL TOTAL MARKET COLLECTION**;
- Terminal is separate from Transportation/Parking;
- Transportation/Parking becomes CT + direct amount, with no required vehicle-class/rate basis;
- vehicle classes/rates move prospectively to Terminal;
- Fish/Meat Vendor Fee becomes independent from NPM/BusinessPayor and uses an independent Fish/Meat vendor registry;
- Weight & Measure requires that registered vendor source;
- the Business Payors product workflow is retired in favor of source-native collection identity;
- NPM Daily may add a reviewed Collect All flow that still posts one Collection/SRC per selected stall;
- Official Monthly Income adjustments are Head-only and signatories are configurable office settings.

Evidence and implementation plan:

- `docs/evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md`
- `docs/planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md`
- IA-067 / IA-068
- ADR-007

No production financial records should be rewritten to make old data resemble the new model. The refactor must be prospective and compatibility-safe.

## Current verified release

New Collection is published with server quotes, stable payer/session/item identities, durable retry, and all returned
Collection/SRC references. V1 preserves one item per Collection; the checkout itself is not revenue. Direct/prepared
ECF and WCF, additional direct Fish/Meat Vendor Fee (IA-064), weighing, NPM Whole Payment and the other supported
adapters are documented in [the full-stack handoff](ITEMIZED_COLLECTION_FULL_STACK_FOLLOWUP.md).

- [CI](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/37274842496),
  [fresh backup](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/37275262538), and
  [production deployment](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/37274842344) succeeded.
- API and portal container images both carry the full SHA above. API health and portal login return 200; the deployed
  scoped CSS has balanced braces. These checks did not create or alter production financial records.
- [Signed publication](https://github.com/Clinttttt/EEMO-Cantilan-SDS/actions/runs/37295944046) succeeded.
  The [APK](https://github.com/Clinttttt/EEMO-Cantilan-SDS/releases/tag/collector-1.1.12-14) downloads successfully
  (46,068,927 bytes). Its inspected Android manifest reports 1.1.12 / 14; the API advertises the same values.
- APK SHA-256: `f3277e31c897d0ad2305a68fe8560979772b5bc96904f942b4dc1efe49a812fe`.
- Interactive Windows/narrow-width review is still a separate gate: computer/browser surfaces were unavailable to
  this session. Component tests and a Windows Release compile passed; they are not visual approval.

The October 4 sections below remain historical evidence. Their unmerged/unpublished status, older source rollout and
blocked Mobile-by-Payor statements are superseded by this release, current code/tests and IA-062/IA-063/IA-064.

## 1. Historical 2026-10-04 repository / release position

- Local branch: `release/v3-final-closure`.
- Before this documentation-only commit, the branch was three commits ahead of `origin/master` at this checkpoint:
  - `6b0cae4d` — Monthly Income ignores canonical shadow money where the source authority map says legacy money still counts.
  - `2d8e5612` — Tabo report body aligned with the V3 report language.
  - `70acb572` — final-closure checkpoint recorded in the RC gap audit.
- Those three commits were not pushed, merged, deployed, migrated, published or version-bumped at this checkpoint.
- AF No. 51 accountability and multi-line OR integration (IA-061), local only, on top of the documentation commits:
  - `b1102c8a` — exact printed serial identity, range registration, Lost state, loss reports, follow-up references, position/history/RAAF-support reads, next-expected receipt order, additive migration `AddAf51SerialIdentityAndAccountabilityEvents`.
  - `0041e953`, `7874c24b` — Accountable Forms UI (register by printed serial, cancellation, loss by copy, exceptions, history, accountability support) and the next-assigned-receipt picker on Current Collection and ECF.
  - `86f8e6a5` — new tables added to tenant backup/export; architecture allow-list.
- PR #27 and PR #28 are already merged; no open PR remains from that earlier closure work.

## 1A. SRC-first collection pivot (IA-062, 2026-10-04) — local, uncommitted to any remote

Direction change from Clint: the **StallTrack Reference Code (SRC)** — `SRC-YYYY-NNNNNN`, a global monotonic database sequence — is the primary digital identity of every canonical Collection, and a physical OR/CT serial is no longer a required input anywhere.

- **Done (this branch):** additive migration `AddCollectionReferenceCode` (sequence, `ReferenceYear`/`ReferenceNumber`/stored `ReferenceCode`, unique indexes, deterministic backfill by `RecordedAtUtc, Id`); posting for Web Current Collection/ECF, WCF Mobile and governed services no longer takes, validates or consumes a document; DTOs, Collection Activity, Collections register/detail, remittance (scope/detail/submission), collector report, fines register, governed/WCF/ECF activity, Mobile records and offline sync results all carry the SRC; OR-selection, "available receipts", "available Cash Tickets" and per-operation documents endpoints removed; WCF/governed readiness no longer depends on CT/OR custody; tenant restore carries the SRC verbatim (generated column is not inserted, sequence re-seeded).
- **Preserved:** legacy OR/CT APIs, imports and evidence; source authority (no cutover); Accountable Forms (AF No. 51) as an **optional back-office register** with its schema and history; pre-SRC `ClientOperationId`s still replay (same amount).
- **Accountable Forms** no longer implies collection consumption; Issued/Consumed counts now reflect only forms the office recorded as issued.
- **Superseded note:** the cross-day SRC search and the SettlementCutover inventory gate listed here as open were resolved in Phase 2 (see 1B below); Mobile Electricity and the other legacy writers remain legacy.

## 1B. Consolidation checkpoint (2026-10-04, documentation only)

- **Authority:** IA-062 (SRC-first) governs collection identity. SRC is not an Official Receipt, Cash Ticket or government receipt; physical OR/CT (AF No. 51 for ORs, suffix semantics still open under IA-059) remain real-world evidence and instrument policy, tracked in the optional Accountable Forms register.
- **Not universal yet:** SRC-first applies to already-canonical writers (Web Current Collection/ECF, WCF Mobile, governed services). Legacy Mobile writers (Market daily, Monthly Collection, Taboan, Terminal, Electricity and other facility writers) still take a typed OR number and stay legacy until a controlled prospective cutover; their historical OR evidence is untouched.
- **Phase 2 status:** `SettlementCutoverWorkflow` no longer gates on physical inventory (informational only) and Collection Activity has a server-side cross-day SRC lookup. Every remaining Mobile OR-entry screen is class D (no approved canonical writer/rules); see `SRC_PHASE2_MOBILE_OR_AUDIT_20261004.md`.
- **Local dev data:** the local dev DB holds a test collection (payer "SRC runtime check", SRC-2026-000006). It is review data, not evidence; SRC gaps are valid, so do not renumber.
- **Naming:** current office name is MEEDO; older records may keep the historical EEMO terminology (see `docs/README.md`).

## 2. Validation state

Latest completed validation reported for the branch:

| Check | Result |
|---|---|
| Unit tests | 2,495 passed |
| Component tests | 692 passed |
| PostgreSQL integration | 210 passed, 7 snapshot-gated skipped |
| Solution Release build | 0 errors |
| Mobile Android Debug | 0 errors |
| Mobile Android Release | 0 errors |
| Production health | unauthenticated `GET /health` returned 200 / `{"status":"ok"}` |

The seven skipped tests require a restored local snapshot through `STALLTRACK_SNAPSHOT_DB`; they remain intentionally opt-in.

## 3. Current financial / source-authority direction

- Legacy and canonical representations coexist source-by-source. Exactly-once reporting follows explicit source authority rather than amount/date/payor deduplication.
- Official Monthly Income now applies the same source-authority rule used by Collection Activity when deciding whether a canonical line counts.
- Two distinct genuine collections that happen to share the same amount/date/payor still both count.
- Remittance does not create revenue and remains separate from accountable-form custody.
- Cash Ticket stock may remain assigned after money collected with earlier tickets has been remitted.
- WCF is the current canonical Mobile payor-keyed collection path: Cash Ticket, direct amount, optional office preparation, one-time tenant enablement.
- Governed services use server-approved policy/rates and may use configured fee types. Walk-up payer text is not Payor identity.

## 4. Official Receipt / AF No. 51 — newly confirmed office evidence

On 2026-10-04 the MEEDO Head confirmed that Accountable Form No. 51 is the Official Receipt currently used by MEEDO.

Confirmed operational facts now recorded in `docs/evidence/2026-10-04_meedo_head_af51_official_receipt_confirmation.md`:

- collectors physically hold the OR booklets used in current collection;
- one OR may contain several compatible charges for the same payor, with each charge kept as its own Nature of Collection line;
- StallTrack is not replacing the physical OR issuance process;
- the exact printed receipt identifier, including any suffix, must be preserved and not inferred.

Authoritative DOF/BLGF/COA research is now recorded in `docs/evidence/2026-10-04_af51_authoritative_philippine_rules.md` and IA-060. It confirms the national baseline for strict serial sequence, Local Treasurer custody/provenance, collector-held accountable forms, quantity/range accountability, monthly RAAF/CRAAF reporting, non-reuse of spoiled/cancelled forms, and immediate notice/control for lost forms. The only serial-identity question left open is the semantic meaning of a trailing printed suffix such as `A` (IA-059); exact printed identity is preserved regardless.

**Implemented since (IA-061):** the AF No. 51 inventory and accountability model is now in code: exact printed serial with a normalized unique key, range registration with a counted quantity, collector custody and transfer, cancellation and loss (whole set or by copy) that never return to stock, "Needs follow-up" until the external RCD or notice reference is added, position/history/accountability-support views, and a next-assigned-receipt picker. One OR with several lines (rent, ECF, penalty) already worked through the Composer for sources on Canonical settlement authority; sources still on Legacy authority were deliberately NOT integrated because that needs a controlled cutover. No cutover, no Treasurer step, no production change, and no change to remittance or revenue reporting were made.

## 5. Current UI / reporting state

- Web Office V3 presentation is the active visual direction.
- Tabo report body has been moved to the current V3 report language, but has not yet received the requested rendered browser review.
- Revenue Setup, vendor drawer, Follow-up Queue, WCF/ECF statements, remittance screens and affected Mobile screens likewise still need the human rendered pass.
- Android Debug/Release builds compile, but the current checkpoint did not run the app on a physical Android device.
- The deployed tenant's stored office name could not be authenticated/read. Code defaults and seeds use MEEDO; if production Office Profile still stores the older office name, update it administratively rather than through a data migration.

## 6. Open decisions / genuine remaining gaps

1. **Cash Ticket denomination policy:** the open sub-gate recorded under IA-056 still requires office confirmation. Do not infer printed denominations from the current serial-number model.
2. **AF No. 51 printed serial suffix meaning:** IA-059 remains open. Preserve the exact printed value; no suffix-dependent automation is authorized. This does not block the AF No. 51 accountability implementation under IA-057/IA-058/IA-060.
3. **Source-native Mobile New Collection refactor:** the 2026-10-06 target is confirmed (IA-068 / ADR-007) but is not part of the Oct 5 production release. Replace the Business-Payor-first discovery contract with typed source-native search and server-returned eligible items. Do not attach operations by payer-name matching.
4. **Collection Activity:** still has an older legacy feed boundary in the Web page; the backend/current-report contract follow-up remains.
5. **Annual targets / official adjustments:** governance is now resolved for the bounded annual-target/report-revision model by IA-066. The remaining gap is implementation/integration/release of those contracts and UI where not already present on the active integration branch; do not invent target values.
6. **Mobile Electricity:** legacy-path/cutover decision remains open.
7. **Snapshot validation:** run the seven snapshot-gated tests against a restored local production snapshot before final release sign-off.
8. **Rendered review:** complete localhost / Windows-Mobile review and an Android runtime check on an appropriate device or installed system image.
9. **AF No. 51 sequence (IA-061) — superseded for collection by IA-062:** collection screens no longer offer a next receipt; sequence/skipped review applies only inside the optional Accountable Forms register. Whether the office wants a hard block there remains an open ruling.
10. **Multi-line OR for Legacy-authority sources:** rent, ECF and similar rows still on Legacy settlement authority cannot join a canonical multi-line OR until a controlled source cutover is approved and run.
11. **Mobile OR/source-discovery paths:** Mobile's Electricity OR remains the legacy typed-number path (a legacy writer awaiting its own cutover, not an SRC path). The confirmed source-native New Collection refactor is also not in the Oct 5 production release.
12. **AF No. 51 rendered review:** the new Accountable Forms views were reviewed from real components with the compiled CSS in headless Chrome (desktop and 390px), not through a logged-in session against a seeded database.
13. **Migration pre-check:** before applying `AddAf51SerialIdentityAndAccountabilityEvents` to a database with existing accountable documents, confirm no two documents of one instrument differ only by case or whitespace.

## 7. Documentation-use rule

Do not infer current implementation state from an older dated handoff or phase note by itself.

When implementation status matters, use this order:

1. this file;
2. current Git/code/migrations/tests/workflows;
3. current Decision Registry and Operational Rulebook;
4. dated planning/handoff documents as historical evidence.

Historical phase labels in older documents are not automatically current release status.
