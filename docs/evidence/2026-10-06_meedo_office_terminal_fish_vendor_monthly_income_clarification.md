# MEEDO office clarification — Monthly Income continuation, Terminal, Transportation/Parking, Fish/Meat registry, and collection workflow

**Date recorded:** 2026-10-06
**Authority:** Direct clarification from Cantilan MEEDO staff/Head during office review, accompanied by office reference sheets supplied to the StallTrack project.
**Status:** Current Cantilan business authority. Where this evidence conflicts with earlier StallTrack planning assumptions, the later 2026-10-06 clarification governs prospectively. Historical records are not rewritten merely because the target workflow changed.

## Evidence files

- `2026-10-06_terminal_income_monthly_report.png` — continuation page of the office Monthly Income report showing Terminal and Slaughterhouse sections and the closing overall total/signatories.
- `2026-10-06_fish_retailing_business_table.png` — office Fish Retailing business list used as evidence for the independent Fish/Meat vendor registry context.

The photographed sheets are evidence of office structure and terminology. Handwritten or printed historical totals are not automatically transaction-level source data.

## 1. Official Monthly Income continues beyond the previously captured page

The previously implemented report was incomplete. The office report continues with:

### B. Income From Terminal

1. **COMFORT ROOM**
2. **PULL PUL VANS, CARGO VANS**
3. **TRICYCAD**
4. **Total Income from Terminal**

### C. Income from Slaughterhouse

Slaughterhouse is a separate top-level income section.

The report then closes with:

- **OVERALL TOTAL MARKET COLLECTION**
- **Prepared by**
- **Certified Correct**

The official report therefore has three top-level income families for the current Cantilan reference:

- **A. Income From Market**
- **B. Income From Terminal**
- **C. Income from Slaughterhouse**

The existing Market page may continue to contain its internal Rent Income and Space Rental subsections. Those do not replace the newly confirmed B and C sections.

## 2. Terminal is not Transportation / Parking

The earlier StallTrack assumption that current Transportation/Parking and Transport Terminal/TRM are one operation is superseded.

**Income From Terminal** is its own operation/report family. **Transportation / Parking** is a separate collection source.

Do not use the label `TRM` as a synonym for Transportation/Parking in the target model.

Legacy TRM/Transport Terminal records remain historical compatibility evidence until a deliberate migration/retirement plan is implemented. Do not rewrite historical records by name.

## 3. Terminal collection rules

All three Terminal income sections use **Cash Ticket (CT)**.

### a. COMFORT ROOM

- direct amount collection is allowed;
- no per-person or per-use row is required for the office's high-volume workflow;
- the peso total alone is sufficient;
- Cash Ticket count may be captured as optional supporting evidence.

### b. PULL PUL VANS, CARGO VANS

- direct total amount collection is allowed;
- the peso total alone is sufficient;
- Cash Ticket count may be captured as optional supporting evidence;
- the office vehicle classes that belong here are:
  - Jeepney
  - Multicab
  - Van
  - Public Utility Bus
  - Public Utility Baby Bus

Existing vehicle-class/rate configuration belongs to the Terminal context, not Transportation/Parking. Vehicle-class rates may support an itemized/assisted collection path, but they are not required evidence for the office-approved aggregate total entry.

### c. TRICYCAD

- direct total amount collection is allowed;
- the peso total alone is sufficient;
- Cash Ticket count may be captured as optional supporting evidence;
- Tricycle belongs to this section.

Use the office's printed wording **PULL PUL VANS, CARGO VANS** and **TRICYCAD** in the formal Monthly Income report unless MEEDO supplies a corrected official form.

## 4. Transportation / Parking rule

Transportation / Parking remains **Cash Ticket** but is independent from Terminal.

Current clarified rule:

- no vehicle-class rate basis is required;
- no terminal/TRM dependency is required;
- the collector records the **amount received directly**;
- payer/reference may remain optional where the operation permits it;
- detailed vehicle counts/classes are not required financial evidence.

The current vehicle-class/rate setup under Transportation/Parking is therefore a stale placement for the target design.

## 5. Fish / Meat Vendor Fee is independent from NPM

Fish / Meat Vendor Fee is not an NPM stall-rent feature and does not require an NPM occupancy.

The office maintains a separate vendor-registration context. A registry record should represent at least:

- vendor/business name;
- vendor type: **Fish** or **Meat**;
- annual/tax-year context;
- registration status such as **New** or **Renew**;
- other office registration fields where evidence supports them.

A registration is one vendor type at a time. Do not silently mark one registration as both Fish and Meat.

### Collection rule

- Fish / Meat Vendor Fee remains its own revenue classification;
- there is **no approved fixed fee rate** in the current clarification;
- the collector enters the **actual amount received**;
- repeated legitimate collections are allowed where they represent real office collections;
- current OR policy remains unchanged unless MEEDO later changes it.

A Fish/Meat fee may be recorded for a named vendor who is not yet registered in StallTrack. In that case the entered name is historical payer/vendor snapshot evidence; StallTrack must not fabricate a permanent registry link merely because the text matches a later record.

## 6. Meaning of the Fish Retailing paper totals

The handwritten amounts at the right side of the Fish Retailing reference are **monthly report totals** for the referenced month.

They are not:

- approved vendor rates;
- annual/YTD balances;
- one individual transaction per vendor;
- evidence that the amount should be imported as a new Collection.

Historical import must therefore preserve these values only as monthly summary/reference evidence unless transaction-level source records are separately available.

## 7. Weight & Measure / Registration

Weight & Measure remains a separate revenue classification from Fish / Meat Vendor Fee.

However, the source person/vendor must come from the independent Fish/Meat vendor registry.

Confirmed behavior:

- vendor selection is required;
- free-text unregistered vendor fallback is **not** allowed for Weight & Measure;
- vendor type determines the applicable weighing context;
- Fish and Meat remain separate types;
- quantity/rate/amount evidence stays with Weight & Measure and never settles Vendor Fee or rent;
- Weight & Measure may remain available as a standalone Mobile route and may also be offered inside unified New Collection for an eligible registered vendor.

The earlier NPM-linked Fish/Meat source requirement is superseded prospectively.

## 8. Unified New Collection — source-native discovery

The target Mobile collection workflow is no longer centered on a generic Business Payor master record.

Search should discover **source-native identities**, for example:

- NPM occupants/stallholders;
- monthly renters/space holders;
- Fish/Meat vendor registrations;
- utility account subjects;
- other approved operation-owned identities.

Selecting a person/vendor/occupant must show only the operations that the authoritative source data makes eligible.

Examples:

- a registered Fish vendor may be offered **Fish / Meat Vendor Fee** and **Weight & Measure**;
- a Kanmanggay holder may be offered the applicable monthly space-rental obligation;
- an NPM occupant may be offered today's NPM Daily charge, Whole Payment when valid, and only those utilities that actually exist for that source.

Do not show every operation merely because a name was searched.

If no source relationship exists, StallTrack may offer only direct/one-off operations whose own policy allows an optional/free-text payer. This fallback must never manufacture rent, utility, weighing or other relationship-backed eligibility.

## 9. Business Payor product feature is retired from the target workflow

The office clarification removes the need for a separate Business Payors page/workflow as a prerequisite to ordinary collection.

Target product direction:

- retire the Business Payors page and explicit manual-linking workflow;
- remove “Needs Payor” as a normal operational prerequisite where a source-native identity already exists;
- keep posted payer/vendor/occupant snapshots for history;
- never auto-merge identities solely by matching names.

Implementation may temporarily retain existing `BusinessPayor` persistence for compatibility while source-native contracts are migrated. Temporary storage compatibility must not be treated as continued product authority.

## 10. NPM Daily “Collect All”

NPM Daily may provide a fast **Collect All** workflow for today's daily fee only.

Confirmed rules:

- it collects **today's daily charge** for selected stalls;
- it does not collect the full monthly/Whole Payment amount;
- the default reviewed list may start with all currently eligible stalls selected;
- staff may uncheck exceptions/absent payers;
- unchecked stalls remain unpaid for the day;
- every selected payer/stall still receives its own Collection/SRC;
- no synthetic aggregate Collection replaces the individual histories.

The office did **not** approve a comparable Collect All behavior for monthly rentals such as TCC/NCC/BBQ/ICE/Kanmanggay. Those remain individual because real payment behavior is irregular.

## 11. Official Monthly Income governance

The Head may correct the official Monthly Income presentation when the computed amount is not the office-approved figure.

This must be implemented as an **audited report adjustment**, not by editing or deleting posted Collections.

Required evidence:

- system-calculated amount;
- official adjusted amount or signed delta;
- required reason;
- optional reference;
- Head actor;
- timestamp;
- immutable revision history/supersession.

Only the **Head** is authorized for this adjustment workflow.

Annual targets remain approved office figures and are not inferred from history.

## 12. Report signatories

Prepared-by and Certified-Correct signatories are configurable office settings, not hard-coded people.

The current paper shows:

- **JED O. GANANCIAS — Admin. Aide III**
- **RODANIE D. GUAZON — Market Supervisor IV**

These names/positions are reference evidence only. Configuration must allow future changes.

For a finalized/exported historical report, preserve the signatory snapshot used for that report so later settings changes do not silently rewrite old official output.

## 13. Superseded StallTrack assumptions

Do not reintroduce these assumptions:

- Transportation/Parking = TRM/Terminal.
- Transportation/Parking requires vehicle-class rates.
- Fish/Meat Vendor Fee is linked to NPM occupancy.
- Fish/Meat Vendor Fee requires a Business Payor.
- Weight & Measure requires an NPM Fish/Meat stall.
- Business Payor is the mandatory cross-operation identity for unified collection.
- Monthly-rental Collect All is approved.
- The previously implemented single-page Monthly Income structure is complete.

## 14. Implementation boundary

This evidence changes target business behavior but does not authorize silent historical rewriting.

The implementation plan must:

- preserve existing posted Collections/SRCs and classifications;
- introduce new source-native registry/collection contracts prospectively;
- migrate UI/workflows deliberately;
- retire stale pages/configuration only after replacement paths are tested;
- keep classification, remittance and audit boundaries intact.

See the corresponding Decision Registry entries and the 2026-10-06 refactor plan before changing financial code.
