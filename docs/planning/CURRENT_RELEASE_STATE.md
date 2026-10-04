# StallTrack Current Release State

**Status:** Current implementation checkpoint
**As of:** 2026-10-04
**Code basis:** `release/v3-final-closure` through `70acb572` before this documentation-reconciliation commit
**Production baseline:** `origin/master` at `cdcd3701`

This is the single current implementation-status entry point. Dated audits, handoffs and historical phase documents remain useful evidence, but they must not be used as the current release state without checking this file, current code/tests and Git history.

## 1. Repository / release position

- Local branch: `release/v3-final-closure`.
- Before this documentation-only commit, the branch was three commits ahead of `origin/master` at this checkpoint:
  - `6b0cae4d` — Monthly Income ignores canonical shadow money where the source authority map says legacy money still counts.
  - `2d8e5612` — Tabo report body aligned with the V3 report language.
  - `70acb572` — final-closure checkpoint recorded in the RC gap audit.
- Those three commits were not pushed, merged, deployed, migrated, published or version-bumped at this checkpoint.
- PR #27 and PR #28 are already merged; no open PR remains from that earlier closure work.

## 2. Validation state

Latest completed validation reported for the branch:

| Check | Result |
|---|---|
| Unit tests | 2,478 passed |
| Component tests | 681 passed |
| PostgreSQL integration | 194 passed, 7 snapshot-gated skipped |
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

No new OR/accountable-form writer or source cutover was implemented by this documentation pass.

## 5. Current UI / reporting state

- Web Office V3 presentation is the active visual direction.
- Tabo report body has been moved to the current V3 report language, but has not yet received the requested rendered browser review.
- Revenue Setup, vendor drawer, Follow-up Queue, WCF/ECF statements, remittance screens and affected Mobile screens likewise still need the human rendered pass.
- Android Debug/Release builds compile, but the current checkpoint did not run the app on a physical Android device.
- The deployed tenant's stored office name could not be authenticated/read. Code defaults and seeds use MEEDO; if production Office Profile still stores the older office name, update it administratively rather than through a data migration.

## 6. Open decisions / genuine remaining gaps

1. **Cash Ticket denomination policy:** the open sub-gate recorded under IA-056 still requires office confirmation. Do not infer printed denominations from the current serial-number model.
2. **Mobile Collect by Payor:** not built. Current canonical Mobile payor identity exists for WCF; governed walk-up services cannot be attached to a Payor by name. A truthful combined workflow needs a payor-linked collectible-item contract and/or authorized Rent/ECF/penalty canonical cutover.
3. **Collection Activity:** still has an older legacy feed boundary in the Web page; the backend/current-report contract follow-up remains.
4. **Annual targets:** governance/source/revision policy remains unresolved; do not invent values.
5. **Mobile Electricity:** legacy-path/cutover decision remains open.
6. **Snapshot validation:** run the seven snapshot-gated tests against a restored local production snapshot before final release sign-off.
7. **Rendered review:** complete localhost / Windows-Mobile review and an Android runtime check on an appropriate device or installed system image.

## 7. Documentation-use rule

Do not infer current implementation state from an older dated handoff or phase note by itself.

When implementation status matters, use this order:

1. this file;
2. current Git/code/migrations/tests/workflows;
3. current Decision Registry and Operational Rulebook;
4. dated planning/handoff documents as historical evidence.

Historical phase labels in older documents are not automatically current release status.
