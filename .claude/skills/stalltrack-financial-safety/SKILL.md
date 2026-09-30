---
name: stalltrack-financial-safety
description: Financial-risk review for StallTrack backend changes touching Collections, lines, allocations, accountable documents (OR/CT), sources/assessments, rates, reports, Payors, corrections or cutover. Use before committing any such change.
---

# StallTrack financial safety review

For each item, answer from the diff and tests — "not applicable" needs a reason.

| Risk | What to check |
|---|---|
| Duplicate settlement authority | Does anything besides the owning source (or the canonical coordinator for a Canonical source) now change what is owed/settled? Are compatibility projections written only inside canonical posting? |
| Double counting | Can the same money appear twice (legacy row + Collection, projection + line, opening settlement as cash, allocation summed per line and per collection, correction applied twice)? |
| Source vs classification confusion | Is a facility/source label used as a revenue classification, or a classification seed used as proof of a source/writer? Stall rent vs Fish/Meat Vendor Fee vs Weight & Measure kept apart? |
| Assessment vs collection | Is an assessment/obligation amount being reported as cash, or cash inferred from a status flag? |
| Physical-document reuse | Can an issued/consumed/reconciliation-required OR/CT become available, or be issued under a new key? |
| Idempotency conflict | Same key + same intent → durable outcome only after authorization; same key + changed intent → explicit conflict; concurrent identical → one effect. |
| Historical evidence mutation | Are posted Collections, lines, issued documents, frozen snapshots or old policy rows edited/deleted? Corrections must append. |
| Tenant leakage | Every query filtered by resolved tenant; cross-tenant ids answer NotFound; no unexplained `IgnoreQueryFilters`. |
| Cross-period correction | AsOf uses RecordedAt cutoff; LatestCorrected follows corrections to the original period; no official RCD treatment chosen. |
| Fake zero/default values | Missing rate/policy/evidence surfaces as unavailable/unresolved, not `0m` or a guessed constant. |
| Arbitrary collector-entered rates | Rate, classification, instrument and charge identity are server-resolved from approved policy. |
| Implicit Payor merging | No linking by name, phone, OR/CT number or similar spelling. |
| Uncontrolled cutover | No source authority state change, activation, backfill or report switch outside the Q43 gate. |

If any row fails, fix it or stop and record the gap as BLOCKED. Do not commit a known financial risk.
