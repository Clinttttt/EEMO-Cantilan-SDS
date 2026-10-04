# StallTrack Current Release State

**Status:** Current implementation checkpoint
**As of:** 2026-10-04
**Code basis:** `release/v3-final-closure` through the AF No. 51 accountability commits listed in section 1
**Production baseline:** `origin/master` at `cdcd3701`

This is the single current implementation-status entry point. Dated audits, handoffs and historical phase documents remain useful evidence, but they must not be used as the current release state without checking this file, current code/tests and Git history.

## 1. Repository / release position

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
- **Open / not done here:** cross-day SRC search on Collection Activity (the page filters the loaded day only; a server-side search parameter is the follow-up); `SettlementCutoverWorkflow` still reports accountable-document inventory as a cutover prerequisite for converting a legacy source (a source-cutover concern, intentionally untouched); Mobile Electricity (typed OR number) and the other legacy NPM writers keep their legacy path until their own cutover; Mobile Android runtime review is still pending.

## 1B. Consolidation checkpoint (2026-10-04, documentation only)

- **Authority:** IA-062 (SRC-first) governs collection identity. SRC is not an Official Receipt, Cash Ticket or government receipt; physical OR/CT (AF No. 51 for ORs, suffix semantics still open under IA-059) remain real-world evidence and instrument policy, tracked in the optional Accountable Forms register.
- **Not universal yet:** SRC-first applies to already-canonical writers (Web Current Collection/ECF, WCF Mobile, governed services). Legacy Mobile writers (Market daily, Monthly Collection, Taboan, Terminal, Electricity and other facility writers) still take a typed OR number and stay legacy until a controlled prospective cutover; their historical OR evidence is untouched.
- **Phase 2 items:** (a) `SettlementCutoverWorkflow` still treats accountable-document inventory reconciliation as a cutover prerequisite — contradicts IA-062, not yet fixed; (b) the legacy Mobile cutovers above; (c) cross-day SRC lookup (search currently filters the loaded day only).
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
3. **Mobile Collect by Payor:** not built. Current canonical Mobile payor identity exists for WCF; governed walk-up services cannot be attached to a Payor by name. A truthful combined workflow needs a payor-linked collectible-item contract and/or authorized Rent/ECF/penalty canonical cutover.
4. **Collection Activity:** still has an older legacy feed boundary in the Web page; the backend/current-report contract follow-up remains.
5. **Annual targets:** governance/source/revision policy remains unresolved; do not invent values.
6. **Mobile Electricity:** legacy-path/cutover decision remains open.
7. **Snapshot validation:** run the seven snapshot-gated tests against a restored local production snapshot before final release sign-off.
8. **Rendered review:** complete localhost / Windows-Mobile review and an Android runtime check on an appropriate device or installed system image.
9. **AF No. 51 sequence (IA-061) — superseded for collection by IA-062:** collection screens no longer offer a next receipt; sequence/skipped review applies only inside the optional Accountable Forms register. Whether the office wants a hard block there remains an open ruling.
10. **Multi-line OR for Legacy-authority sources:** rent, ECF and similar rows still on Legacy settlement authority cannot join a canonical multi-line OR until a controlled source cutover is approved and run.
11. **Mobile OR paths:** Mobile's Electricity OR remains the legacy typed-number path (a legacy writer awaiting its own cutover, not an SRC path); Mobile Collect by Payor is still not built.
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
