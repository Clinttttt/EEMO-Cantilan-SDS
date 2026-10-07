# StallTrack V3 — 2026-10-06 Office Clarification Refactor Plan

**Status:** Approved business/refactor baseline; substantially implemented in the accepted local integration checkpoint as of 2026-10-07, with production rollout/activation still separate.
**Authority:** [2026-10-06 MEEDO office clarification](../evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md).
**Current implementation checkpoint:** `integration/report-governance-ui` at `6d3362f9`; see `CURRENT_RELEASE_STATE.md` and the October 7 handoffs for exact verified scope.
**Purpose:** Give frontend/backend agents one precise target after the office clarified Terminal, Transportation/Parking, Fish/Meat registration, Weight & Measure, Business Payor retirement, NPM Daily batch collection, and the complete Official Monthly Income structure.

> Implementation note: the major contracts in this plan now exist locally, including source-native discovery, independent Fish/Meat registry/lifecycle/import, Terminal separation, Transportation direct amount, NPM Daily/Whole and Daily Collect All contracts, recent collection correction, and the report topology. Do not interpret this as a production deployment declaration. Source activation (for example NPM canonical Daily where `SourceStillLegacy` still applies), local/production database migration state, deployment, and APK publication remain explicit gates.

## 1. Non-negotiable invariants

The refactor may change source ownership and workflow, but it must preserve:

- posted Collections and immutable SRCs;
- revenue classifications unless the new office evidence explicitly changes grouping;
- remittance as a separate ledger/event;
- report adjustments as report-only revisions, never cash mutation;
- source-owned assessment/balance logic;
- tenant isolation;
- idempotent replay/offline recovery;
- historical evidence without speculative backfill or name matching.

Do not delete or rewrite existing financial records merely because a prior workflow is now superseded.

## 2. Official report topology

Target Official Monthly Income structure:

### A. Income From Market

Keep the existing official Market rows plus its documented Rent Income (Stall Rental) and Space Rental subsections.

### B. Income From Terminal

- a. COMFORT ROOM
- b. PULL PUL VANS, CARGO VANS
- c. TRICYCAD
- Total Income from Terminal

### C. Income from Slaughterhouse

- Slaughterhouse current official amount

### Closing

- OVERALL TOTAL MARKET COLLECTION
- Prepared by
- Certified Correct

Annual target, monthly actuals, total/YTD and percentage remain server-owned official report facts. Head-only audited adjustments and approved target revisions continue to govern official presentation.

## 3. Terminal versus Transportation/Parking

### Terminal

Terminal becomes an independent operation/report family.

All Terminal sections:

- use CT;
- support direct aggregate amount entry;
- may capture Cash Ticket count as optional supporting evidence;
- do not require per-payer/per-vehicle records for aggregate entry.

Vehicle-class configuration moves to Terminal:

- Jeepney → PULL PUL VANS, CARGO VANS
- Multicab → PULL PUL VANS, CARGO VANS
- Van → PULL PUL VANS, CARGO VANS
- Public Utility Bus → PULL PUL VANS, CARGO VANS
- Public Utility Baby Bus → PULL PUL VANS, CARGO VANS
- Tricycle → TRICYCAD

Existing effective-dated rates may remain useful for assisted/individual collection, but aggregate Terminal entry is valid from the direct section total alone.

### Transportation/Parking

Target behavior:

- CT;
- direct amount;
- no vehicle class/rate requirement;
- no TRM label;
- no Terminal dependency.

Current canonical Transportation collections remain historical truth. The implementation must define a prospective boundary rather than moving old money between classifications by guess.

## 4. Fish/Meat independent registry

Create a source-owned Fish/Meat vendor registry independent of NPM.

Minimum target identity:

- stable vendor-registration ID;
- business/vendor display name;
- type: Fish or Meat;
- annual/tax-year context;
- New/Renew status;
- additional office fields only where evidence supports them.

Fish/Meat Vendor Fee:

- OR;
- direct amount received;
- no fixed rate;
- no NPM occupancy requirement;
- no Business Payor requirement;
- registered-vendor selection preferred;
- free-text vendor/payer snapshot allowed when collection occurs before registration exists.

Weight & Measure:

- separate classification and writer;
- requires a registered Fish/Meat vendor;
- no free-text fallback;
- type controls valid weighing context;
- quantity/rate/amount evidence remains frozen and separate from Vendor Fee.

Historical monthly summary values from office sheets are reference/report evidence, not automatically Collection rows.

## 5. Source-native identity and search

The target cross-source search must query source-owned identities rather than a Business Payor master workflow.

A search result should carry:

- source identity kind;
- source record ID;
- display name;
- concise source context;
- eligibility facts needed to request server capabilities.

Examples include NPM occupancy, monthly rental account/occupancy, Fish/Meat vendor registration, utility account and other authoritative source identities.

Do not merge equal names. One person may appear as separate source records until the office explicitly associates them.

## 6. Unified New Collection

New Collection should be relationship-first:

1. search source-native person/vendor/occupant records;
2. choose the intended result;
3. request only source-backed eligible operations;
4. allow one or several compatible items;
5. quote/review using server rules;
6. post each financial child through its existing/source-owned writer;
7. return every Collection/SRC.

For a Fish vendor, the eligible list may include Vendor Fee and Weight & Measure.

For an NPM occupant, it may include Daily, valid Whole Payment, and only utilities that actually exist.

For a Kanmanggay holder, it may include the relevant monthly space-rental obligation.

### Direct fallback

If no registered relationship exists, show only operations whose own server policy allows optional/free-text payer context. Never use the fallback to fabricate rent, utility, weighing or another relationship-backed item.

## 7. Business Payor retirement

Target product state:

- remove Business Payors from normal navigation;
- remove manual Business Payor linking as an operational prerequisite;
- stop presenting “Needs Payor” when the source record itself is sufficient;
- migrate APIs/workflows toward source-native identity.

Compatibility phase:

- existing BusinessPayor tables/IDs may remain temporarily while adapters are converted;
- no new feature should increase dependency on them;
- no destructive migration until current callers, foreign keys, reports and replay contracts are proven migrated.

A dedicated removal migration is a later final step, not the first implementation step.

## 8. NPM Daily Collect All

Approved scope: **NPM Daily only**.

UX contract:

- open today's eligible stall list;
- default eligible rows may be selected;
- staff can uncheck exceptions/absent payers;
- unchecked rows remain unpaid;
- review selected count and total;
- post individual financial children;
- every selected stall gets its own Collection/SRC.

Do not enable this for TCC/NCC/BBQ/ICE/Kanmanggay monthly obligations without a later office decision.

The server should preserve replay safety and return the full child outcome. Do not create one aggregate revenue Collection for the batch.

## 9. Official Monthly Income controls

### Head-only adjustments

Use the existing report-governance concept:

- system basis remains visible;
- adjustment/delta is audited;
- reason is required;
- actor/time/revision retained;
- Collections/remittance/source balances are unchanged.

### Signatories

Add office settings for:

- Prepared by: name + position
- Certified Correct: name + position

Official output should snapshot the signatory values used for a finalized/generated report if the implementation supports persisted report versions.

## 10. Frontend workstream

Frontend must:

- reorganize Mobile operation presentation by official report family;
- replace Business-Payor-first New Collection with source-native search/results;
- implement direct fallback only from server capability flags;
- build Fish/Meat registry and collection surfaces;
- bind Weight & Measure vendor selection to the registry;
- implement NPM Daily Collect All review/exceptions;
- move Terminal vehicle-class setup/UI out of Transportation/Parking;
- simplify Transportation/Parking to direct amount;
- add complete B/C sections, overall total and signatories to Official Monthly Income;
- preserve existing StallTrack V3 visual system; no new design language.

Frontend must not infer eligibility, rates, group placement or identity association.

## 11. Backend workstream

Backend must:

- add/source an independent Fish/Meat vendor registry contract;
- remove NPM/BusinessPayor requirements from Fish/Meat Vendor Fee prospectively;
- require registry identity for Weight & Measure;
- expose source-native unified search and typed identity/capability contracts;
- migrate itemized collection-session payer assumptions safely;
- implement Terminal section classifications/contracts and direct aggregate writer;
- move/re-scope vehicle-class rules to Terminal;
- make Transportation/Parking a direct-amount CT source with a prospective rule/version boundary;
- add NPM Daily batch quote/post/replay with one Collection/SRC per selected stall;
- expose report signatory settings;
- extend Official Monthly Income grouping and totals;
- preserve/report Head-only audited adjustments.

Do not combine the BusinessPayor physical-drop migration with the first functional conversion unless dependency analysis proves it is safe.

## 12. Migration sequencing

Recommended order:

1. Add new report/Terminal/vendor/source-native contracts additively.
2. Add compatibility adapters so old UI/API callers continue to work during transition.
3. Implement Fish/Meat registry and Weight & Measure registry dependency.
4. Implement Terminal and simplified Transportation/Parking.
5. Implement source-native search/New Collection.
6. Implement NPM Daily Collect All.
7. Switch frontend/navigation/report output.
8. Stop creating new BusinessPayor dependencies.
9. Audit remaining BusinessPayor references.
10. Only then remove obsolete product routes and, if safe, persistence.

## 13. Required regression proof

At minimum prove:

- no existing SRC changes;
- old Fish/Meat collections remain readable and classified once;
- new Fish/Meat fee can post without NPM/BusinessPayor;
- Weight & Measure refuses an unregistered vendor and posts for a registered one;
- Terminal aggregate entries report under the exact B section/subline;
- Transportation/Parking direct amount does not report under Terminal;
- existing vehicle-class history is not silently reclassified;
- NPM Daily batch produces N distinct Collections/SRCs for N selected stalls;
- excluded NPM stalls remain unpaid;
- Head report adjustment changes only official report output;
- signatory setting changes do not mutate collection money;
- remittance and void behavior remain unchanged;
- Monthly Income totals count each financial event exactly once.

## 14. Explicit non-goals for the first refactor

Do not:

- retroactively transform historical TRM/Transportation rows by guess;
- import handwritten Fish monthly totals as new Collections;
- auto-create/merge vendors by name;
- implement monthly-rental Collect All;
- make Weight & Measure accept free-text vendor names;
- drop BusinessPayor tables before dependency proof;
- create a second financial writer for itemized checkout;
- change OR/CT instrument policy except where this clarification explicitly states it.
