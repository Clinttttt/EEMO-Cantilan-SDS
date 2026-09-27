# Interim Philippine Reference Basis for Presentation-Only V2 Decisions

**Date recorded:** 2026-09-27
**Status:** PARTIALLY SUPERSEDED / REFERENCE-ONLY PRESENTATION BASIS — **not Cantilan production policy**
**Precedence note:** Direct EEMO Head clarifications recorded later on 2026-09-27 supersede this note for Vegetable/Fruit instrument resolution and the current ECF/WCF presentation. Keep this file only as comparative Philippine reference where Cantilan remains silent.

**Purpose:** Allow the StallTrack V2 presentation and read-only UI audit to proceed while the Cantilan EEMO Head is unavailable. These references may guide safe interface structure and configurable policy design, but they do not replace Cantilan's Local Revenue Code, Market Ordinance, current EEMO policy, or a later direct Head clarification.

## 1. Evidence hierarchy

Use this order when interpreting these interim rules:

1. Direct Cantilan EEMO Head clarification and Cantilan office evidence.
2. Cantilan ordinance/revenue-code evidence when available.
3. Philippine national law for regulatory requirements.
4. Current/recent official LGU ordinances and Citizen's Charters as comparative operating precedent.
5. System-design inference only where clearly marked.

Do not promote an external-LGU example into a Cantilan production rule merely because it is common elsewhere.

## 2. Vegetable / Fruit Space Rental — interim OR-versus-CT basis

### Cantilan-confirmed facts

- Vegetable/Fruit Space Rental may use **Official Receipt (OR) or Cash Ticket (CT)**.
- The EEMO Head described **Cash Ticket as the predominant practice**.
- It is distinct from permanent NPM vegetable-stall tenancy.
- The exact Cantilan condition that selects OR instead of CT is still unconfirmed.

### Philippine operating precedent

Official LGU market rules show a recurring distinction:

- fixed/regular stall rental is documented through an Official Receipt; and
- transient/temporary/market-day space occupancy commonly uses Cash Tickets.

Examples:

- **Naga City Ordinance No. 2004-123** states that an Official Receipt is issued for rentals of fixed stalls, while Cash Tickets are issued to occupants of market premises or transient vendors.
- **Naga City Ordinance No. 2024-020** establishes a Cash Ticket system for transient vendors.
- **Batangas City Market Office Citizen's Charter** uses Cash Tickets for transient vendors / temporary stall holders.

### Interim StallTrack presentation rule

For presentation and UI-audit purposes only, model the likely policy boundary as:

- **regular/fixed recognized rental arrangement -> OR-oriented**
- **temporary/transient/daily/open-space occupancy -> CT-oriented**

This is a defensible Philippine-market pattern, not yet a final Cantilan rule.

The collector must never arbitrarily choose OR versus CT. The target system should resolve the instrument from approved policy/context. Until Cantilan confirms the exact selector, production Vegetable/Fruit cutover remains blocked.

## 3. ECF / WCF assessment basis — interim flexible model

### Cantilan-confirmed facts

- ECF uses **OR**.
- WCF uses **CT**.
- Utility assessment remains source-owned; collection must not invent the amount.

### External reference

**Municipality of Tagbina Ordinance No. 03, Series of 2023** treats electricity and water charges as separate from the regular tent/service charge for certain public-market vendors. For example, build-tent and street-food/sidewalk-vendor charges state that electricity and water bills are excluded and separately billed.

This supports **separate utility assessment**, but it does **not by itself prove one universal computation formula** for Cantilan.

### Interim StallTrack presentation rule

Do not hard-code ECF/WCF as automatically fixed or automatically metered.

The UI/audit should support an approved assessment basis such as:

- **Metered:** actual meter/sub-meter consumption × applicable approved rate;
- **Shared / Allocated:** an approved allocation method derived from an actual common utility bill/reading;
- **Approved Fixed / Manual Approved:** a fixed or manually approved amount backed by an ordinance, rate schedule, bill, or authorized office policy.

Every assessment should preserve the basis/source used. Collector Mobile must not invent the rate or amount.

For **Fiesta/Araw temporary electricity use**, do not automatically reuse the regular ECF formula. If a separate event/temporary rate exists, use that approved policy. Otherwise require another traceable approved basis such as actual metered consumption, allocation of a common bill, or an authorized effective-dated rate.

This remains an interim flexibility rule until Cantilan confirms its exact utility-assessment policy.

## 4. Transfer Large Cattle — interim Philippine regulatory workflow

### National legal basis

**Act No. 1147**, as amended, treats transfer of large cattle as a registered **transfer of ownership/title**, not merely physical transport.

The law requires transfer registration with the municipal treasurer and a certificate of transfer. The transfer record/certificate includes, among other items:

- name and residence of the owner/vendor;
- name and residence of the purchaser/transferee;
- purchase price or consideration;
- class, sex and age;
- brands;
- remolinos/cowlicks and other identifying marks;
- reference to the original certificate of ownership and issuing municipality.

The original certificate of ownership / transfer evidence or other proof of title must be produced before the transfer certificate is issued.

### Current LGU implementation precedent

Recent/current official LGU procedures continue to use this model:

- **Davao City 2026 Citizen's Charter:** Registration and Transfer Fees on Large Cattle are handled by the City Treasurer; AF 53 is required for transfer and AF 52 is issued; the fee is based on the local revenue code.
- **Tuba Municipal Treasurer 2025:** verifies requirements, receives payment, issues the OR, and issues the requested ownership/transfer document.
- Other current LGU revenue codes / charters commonly assess a prescribed transfer fee per head or per certificate and issue an OR.

### Interim StallTrack presentation rule

For presentation and UI-audit purposes, treat Transfer Large Cattle as a **regulatory transfer-of-ownership service** with this provisional shape:

- trigger: registered transfer/change of ownership, not mere transport;
- office/source: Municipal Treasurer / authorized regulatory collection process;
- instrument: **OR-oriented by Philippine operating precedent**;
- fee basis: approved Cantilan Local Revenue Code / ordinance, typically a prescribed per-head/per-certificate fee rather than a percentage of sale price;
- certificate/reference: support AF 52 / Certificate of Transfer and AF 53 / Certificate of Ownership concepts, while allowing Cantilan's actual form names/numbers to be configured;
- one animal record per transferred animal where required.

Capture at least:

- transaction/business date;
- transfer type;
- applicable rate/ordinance code;
- assessed amount and OR reference;
- certificate-of-transfer number/type;
- transferor name/address;
- transferee name/address;
- ownership-certificate reference and issuing LGU;
- animal class/type, sex, age, brand and identifying marks/remolinos;
- consideration/purchase price as regulatory detail, not automatically the fee basis;
- verification/approval status;
- issuing/authorized officer and certificate issuance date;
- configurable signatures/attestations where Cantilan policy requires them.

### Cantilan boundary

The exact Cantilan rate, exact accountable form/certificate numbering, required attestations, and any additional local approval fields remain unconfirmed.

Therefore:

- the **presentation/UI shell may proceed** using the regulatory workflow above;
- the exact fee/rate and final policy remain configurable;
- production posting/cutover must not treat the external examples as Cantilan authority.

## 5. Consequence for the next UI audit

The Sol High read-only UI audit may use these interim rules to avoid blocking the presentation, provided every finding distinguishes:

- **Cantilan confirmed**
- **Philippine-reference / interim presentation basis**
- **still needs Cantilan confirmation**

No UI mock or code review may silently convert an interim reference into a final production rule.

## 6. Public references

- Supreme Court E-Library — Act No. 1147: https://elibrary.judiciary.gov.ph/thebookshelf/showdocs/28/20084
- Davao City 2026 Citizen's Charter, Volume II — Registration and Transfer Fees on Large Cattle: https://davaocity.gov.ph/wp-content/uploads/2026/03/CGD_CC2026_VOLUMEIIE.pdf
- Municipality of Tuba 2025 Municipal Treasurer Citizen's Charter: https://www.tuba.gov.ph/wp-content/uploads/2026/01/MTO-2025.pdf
- Naga City Ordinance No. 2004-123: https://www2.naga.gov.ph/prev-ordinance/ordinance-no-2004-123/
- Naga City Ordinance No. 2024-020: https://www2.naga.gov.ph/sp_ordinances/ordinance-no-2024-020/
- Batangas City Market Office Citizen's Charter: https://www.batangascity.gov.ph/web/ocvascitizen-s-charter/63-citymarketoffice/1083-city-market-office-citizen-s-charter
- Tagbina Municipal Ordinance No. 03, Series of 2023 — Economic Enterprise Code: https://tsppaperless.net/source/32ND%20RS%20FEB%2028%202023/FR%209%201646%20ORD%20NO%2003%20TAGBINA.pdf
