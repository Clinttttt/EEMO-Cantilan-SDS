# 2026-10-04 Authoritative Philippine AF No. 51 / Accountable-Form Research

**Status:** Authoritative Philippine legal/treasury reference research
**Scope:** Accountable Form No. 51 Official Receipts; serial identity; custody; unused ranges; spoiled/cancelled forms; lost forms; reporting/accountability
**Purpose:** Establish the national compliance baseline StallTrack must respect before implementing the MEEDO AF No. 51 workflow. This file does not replace a later Cantilan-specific Treasury instruction where local procedure is more specific and lawful.

## 1. Sources used

Primary / authoritative government sources:

1. **Presidential Decree No. 1445 — Government Auditing Code of the Philippines**, Supreme Court E-Library reproduction:
   https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/26/59533
2. **Local Treasury Operations Manual (LTOM), 2nd Edition, Book II — Receipt and Collection of Income, Revenues and Other Fund Sources**, Department of Finance / Bureau of Local Government Finance:
   https://blgf.gov.ph/wp-content/uploads/2022/10/LTOM-Book-2-E-Copy.pdf
3. **LTOM, 2nd Edition, Book III — Fund Management Practices, Expenditures and Disbursement**, DOF / BLGF:
   https://blgf.gov.ph/wp-content/uploads/2022/10/LTOM-Book-3-E-Copy.pdf
4. **COA Report of Accountability for Accountable Forms (RAAF), Annex 18**, official COA site:
   https://www.coa.gov.ph/wp-content/uploads/ABC-Help/Financial_Management_Brgy/annex18.htm
5. **COA RAAF Instructions, Annex 18.1**, official COA site:
   https://www.coa.gov.ph/wp-content/uploads/ABC-Help/Financial_Management_Brgy/Annex18.1.htm
6. **BLGF Notices of Loss of Accountable Forms**, including current/recent official examples:
   https://blgf.gov.ph/category/notices-of-loss/
   https://blgf.gov.ph/notices-of-loss-of-accountable-forms-for-cy-2023/
   https://blgf.gov.ph/notices-of-loss-of-accountable-forms-for-cy-2025/

The LTOM expressly adopts and organizes applicable LGC/IRR, GAAM, MNGAS for LGUs, COA circulars and other treasury rules. For AF No. 51 implementation, the LTOM is the clearest current DOF/BLGF operational source found.

## 2. Official-receipt baseline

PD 1445, Sec. 68 requires a collecting officer who receives payment to immediately issue an official receipt. Officially numbered receipts are subject to proper custody, accountability and audit.

**StallTrack consequence:** the physical AF No. 51 remains the accountable receipt in MEEDO's confirmed current workflow. StallTrack records and reconciles it; it must not silently replace the physical receipt with an app screen or self-created electronic receipt.

The LTOM further requires pre-numbered official receipts to be issued in **strict numerical sequence**. All copies of a receipt are to be exact copies/carbon reproductions of the original.

**StallTrack consequence:** normal OR use should be sequence-aware. The application may suggest the next registered serial/range position, but must not manufacture a number from one observed receipt when the actual assigned range is unknown.

## 3. What one AF No. 51 serial represents

Official BLGF loss notices repeatedly describe AF No. 51 as a **set** consisting of Original, Duplicate and Triplicate copies under one serial number. BLGF also publishes notices involving only one missing copy (for example a missing duplicate) while referring to the same AF No. 51 serial.

**Implementation rule:** treat one printed OR serial as one accountable receipt **set/unit**, not as three independent receipt numbers.

Recommended representation:

- accountable form type / variant (for example AF 51, AF 51-A, AF 51-C when actually printed on the form);
- exact printed serial identifier;
- optional copy-level exception evidence: Original, Duplicate, Triplicate;
- custody / assignment / use state for the serial set.

Do not count Original + Duplicate + Triplicate as three ORs.

## 4. Serial numbers and suffix letters

Official BLGF notices prove that serial identifiers are not uniformly numeric-only. Examples include:

- AF No. 51 with serial **0495997A** (BLGF CY 2023 notice);
- an AF No. 51 booklet with serial range **9448651 L – 9448700 L** (BLGF CY 2025 notice);
- AF No. 51 with serial **13333499A** (BLGF CY 2021 notice).

BLGF notices also separately refer to **Accountable Form No. 51-A** and **Accountable Form No. 51-C**. Those are printed form designations/variants and must not be conflated with a letter appearing after a serial number.

### Safe StallTrack rule

For the MEEDO specimen such as `2315601 A`:

- preserve the exact displayed identifier as authoritative text;
- a normalized search/uniqueness key may remove presentation whitespace and normalize case, but the raw printed value must remain preserved;
- do **not** infer that the suffix `A` means AF 51-A, a booklet series, a copy type, a fiscal year, or any other semantic category;
- do not strip the suffix;
- do not auto-generate the next suffix;
- do not infer range continuity from one receipt alone.

The authoritative sources found demonstrate that serial suffix letters exist, but **do not establish a universal meaning for the suffix letter**. That meaning therefore remains an explicit unresolved local/printing-source question.

## 5. Local Treasurer custody and collector assignment

LTOM Book II, Section 86 states that the **Local Treasurer is the custodian of all accountable forms requisitioned by the LGU** and must maintain a complete record of receipt, issue and transfer. It also states that an official receipt is held in trust by the collecting officer/treasurer or other duly authorized custodian, who is responsible for safekeeping, proper authorized use, reporting its use/condition, and may be liable for loss/damage caused by negligence.

LTOM further requires:

- a permanent record showing the whereabouts of accountable forms;
- the officer/employee to whom forms were given;
- evidence of receipt and subsequent issuance/use;
- receipt batches recorded with quantity and inclusive serials;
- accountable forms issued to **bonded officers only**, in sufficient quantities **not to exceed three months' use**.

**StallTrack consequence:** the system should record Treasury provenance and collector custody, but this does not require a Treasurer StallTrack account or login. The external legal/accountability relationship can be represented as provenance + custody evidence while MEEDO Admin/Head operates the application.

Do not add a blocking software requirement that a Treasurer must approve every normal collection.

Bonding is an external accountability prerequisite. Unless MEEDO/Treasury asks StallTrack to administer bond records, StallTrack should not invent a mandatory bond-setup workflow that blocks day-to-day collection.

## 6. Unused booklets / ranges

The accountability model is quantity-and-range based, not merely a boolean `Assigned`.

COA's RAAF structure records for each accountable form:

- beginning balance — quantity and inclusive serials;
- receipts — quantity and inclusive serials;
- issuance — quantity and inclusive serials;
- ending balance — quantity and inclusive serials.

LTOM requires accountable officers to render RAAF at the end of the month; the Local Treasurer consolidates the individual reports into CRAAF and submits the consolidated report with individual reports to the COA auditor for verification not later than the fifth day of the following month.

BLGF loss notices commonly identify complete AF No. 51 booklets as **50 sets**, but this is observed practice, not a safe universal software constant.

### Safe StallTrack rule

- register the actual quantity and inclusive serial range supplied;
- never hard-code “50 receipts per booklet” as a financial/accountability rule;
- keep current holder/custodian and full custody history;
- unused forms remain accountable stock;
- returning unused forms to office custody is a custody transfer, not revenue, not cancellation and not remittance;
- do not require an assigned range to be exhausted before money may be remitted (consistent with IA-052).

## 7. Spoiled / cancelled AF No. 51

LTOM Book II states:

- accountable officers must submit obsolete, spoiled and cancelled official receipts/accountable forms to the COA Auditor as prescribed;
- accountable officers must **not destroy accountable forms on their own** and then treat themselves as relieved from responsibility;
- for spoiled/cancelled accountable forms **without fixed money value**, expressly including Accountable Form No. 51, the cancelled **original and duplicate copies** are submitted with the Report of Collections and Deposits, with the cancellation properly noted on the record;
- where damage/cancellation is due to negligence or lack of proper care, appropriate proceedings may be instituted.

### Safe StallTrack state

A spoiled/cancelled OR serial:

- is permanently unavailable for normal reuse;
- creates **no revenue** merely because the form was consumed/cancelled;
- retains exact serial identity;
- records actor, date/time and reason;
- may record evidence/reference that the physical cancellation was included/noted in the RCD process;
- must not be returned to the available serial pool.

StallTrack should not decide legal negligence or disciplinary liability. It records the operational facts/evidence.

## 8. Lost AF No. 51

LTOM Book II, based on GAAM / COA Circular No. 84-233, requires:

1. loss in the custody of a collecting officer to be **immediately reported** to the Treasurer;
2. the Treasurer to issue a circular/notice at once to prevent fraudulent use;
3. the notice to identify:
   - kind of form;
   - quantity;
   - inclusive serial number(s);
   - place(s) of loss;
   - approximate date(s) of loss;
4. the Treasurer may take additional measures such as publication;
5. compliance with the notice requirement is one requirement for any request for relief from accountability.

BLGF continues publishing LGU notices of loss under COA Circular No. 84-233. Recent notices show that loss may involve:

- one full serial set;
- only an Original/Duplicate/Triplicate copy;
- unused forms;
- a partially used booklet;
- an entire unused booklet/range.

### Safe StallTrack loss model

A lost serial/range:

- must never return to Available merely because no collection was posted;
- is blocked from normal issuance/collection;
- preserves exact serial(s) and scope of loss;
- records whether the loss covers the whole set, one copy, multiple sets, or an inclusive range;
- records when/where discovered or lost, who held it, and the reason/narrative;
- provides a place to record the external notice/reference/evidence;
- does not itself grant “relief from accountability”; that remains an external COA/accountability process.

For low-hassle UX, StallTrack can let MEEDO staff record the event and attach/reference the real-world notice without requiring a Treasurer software user.

## 9. Audit / unused-form examination

LTOM Book III instructs examiners to count and list all unused accountable forms on hand, inspect unused booklets to ensure the serial sets are complete, require a written explanation for missing copies, and see that notice of loss is immediately disseminated.

**StallTrack consequence:** accountability views should be able to answer, per collector and form/range:

- what was assigned;
- what is still unused/on hand;
- what was issued;
- what was spoiled/cancelled;
- what was returned/transferred;
- what is lost/missing;
- what needs review.

These are **counts/serials**, not pesos.

## 10. Reporting boundary for StallTrack

StallTrack may generate an operational accountability register or RAAF-style support view from its ledger, but it must not claim that its screen or PDF replaces a prescribed COA/BLGF form unless that replacement is separately authorized.

The data model should be capable of producing the core accountability fields:

- form type / variant;
- beginning quantity/range;
- receipts/assignments/transfers;
- issued/cancelled/lost quantities/ranges;
- ending quantity/range;
- current custodian;
- supporting references.

## 11. Confirmed implementation consequences for the upcoming AF No. 51 task

The national rules plus the direct MEEDO Head evidence support the following implementation direction:

1. **Physical-first:** StallTrack supplements AF No. 51; it does not replace it.
2. **One serial = one receipt set:** Original/Duplicate/Triplicate are copies of the same accountable serial.
3. **Exact identity:** preserve raw printed serial including suffix; do not interpret suffix without authority.
4. **Sequence-aware:** normal issuance follows registered pre-numbered sequence.
5. **Range/custody ledger:** record actual booklet/range quantity, inclusive serials, assignment and transfers.
6. **No hard-coded booklet size:** 50-set booklets are common in BLGF notices but are not encoded as a universal rule.
7. **Collector-held stock:** MEEDO collector custody is compatible with the national model; Treasury remains the formal upstream custodian/accountability authority.
8. **No Treasurer-app dependency:** do not require Treasurer login/approval merely to mirror the legal custody chain.
9. **Cancelled/spoiled = consumed accountability state:** never revenue, never reusable.
10. **Lost = blocked accountability state:** immediate external reporting/notice is required; StallTrack records/supports evidence but does not grant legal relief.
11. **Unused return = custody transfer:** not collection, cancellation or remittance.
12. **Separate ledgers remain mandatory:** form custody, collections, remittance and reporting are related but distinct.
13. **Multi-line OR remains valid:** one physical OR may contain several compatible OR-based collection lines for the same payor/context, as confirmed directly by MEEDO; each line keeps its own revenue identity.
14. **Copy-level exceptions should be representable:** BLGF notices show loss of only one copy of a serial set, so the model should not assume every exception affects all three copies.

## 12. Still unresolved after authoritative research

The research did **not** establish:

- a universal semantic meaning for a suffix letter such as `A` after an AF No. 51 serial;
- that the suffix on MEEDO's specimen identifies AF 51-A;
- that every AF No. 51 booklet universally contains 50 sets;
- a requirement for StallTrack itself to administer fidelity bonds;
- authority for StallTrack to replace the physical AF No. 51 with a computerized/electronic receipt;
- any basis for reusing a spoiled/cancelled/lost serial.

The safest system rule is therefore to preserve actual printed identity and actual physical custody evidence, enforce non-reuse for exception states, and avoid inventing unsupported semantics.
