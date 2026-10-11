# MEEDO three-year occupancy contract clarification

Received: October 9, 2026. Source: direct clarification from MEEDO office personnel, relayed by the project owner in the current task. This records authoritative local office direction, with operational details still to be confirmed. Signed contracts were not independently inspected; this is not a national-law claim.

## Confirmed rule and scope

The standard occupancy contract runs for **three years** in **Kanmanggay, NPM, NCC and TCC**. Contract duration is separate from rental assessment and payment frequency. Monthly rent remains monthly; authorized NPM Daily collection remains daily under the underlying occupancy agreement. Expiration is not unpaid status, closure, renewal or rate approval.

Vegetable/Fruits, BBQ, Ice Plant, Fiesta/Araw and custom/temporary rentals are **not confirmed** within this rule. Shared UI does not extend contract policy to them. No historical commencement, expiry, reference or renewal approval may be fabricated. Unknown expiry is not an approved indefinite contract.

This clarification establishes the standard term; it does not resolve previously accepted Space Only/Extension exceptions or authorize automatic expiration, changes to billing, or historical backfill. Preserve those arrangements and controls until MEEDO resolves the exception policy.

## Current capability matrix (code audit, not new implementation)

| Source | Stored contract information | Duration / expiry | Renewal and history | Closure / reopening | API / UI limitations |
|---|---|---|---|---|---|
| NPM | `Contract` per physical `Stall`: identity, actual occupant, name on contract, effectivity, duration, arrangement, contract rate, active/ended dates and audit fields | Variable positive years; expiry derived by `DomainRules.TermLastDay`, not a stored expiry. Not enforced as three years | Existing renew command appends a Contract, terminates prior active terms and retains historical rows/rates | Stall status/ClosedAt; existing status-reopen handler excuses eligible closure days/months | Stall create/update, status, renew and historical projections exist. UI exposes duration choices, expiry/current/closed status and history. Existing rates and daily/month basis remain authoritative |
| NCC | Same Stall/Contract model | Same variable duration and exact-term expiry | Same append-term renewal/history | Same stall closure/status path | Same API family; document prints verified contract fields. No three-year-only validation |
| TCC | Same Stall/Contract model | Same variable duration and exact-term expiry | Same append-term renewal/history | Same stall closure/status path | Same API family and term/document support. No three-year-only validation |
| Kanmanggay | Source-owned `ObligationAccount`: actual occupant, signed-contract name/reference, optional exact commencement and descriptive years, optional area/location, SignedContract/SpaceOnly basis, ActiveFrom/ActiveTo; effective-dated `ObligationRate` records | Later October 9 evidence implementation stores exact commencement separately from billing month and permits only three years when a new KM signed term is recorded. Historical unknown evidence remains null. No enforced expiry or complete term chain | No renewal endpoint or term history; account periods/rates and collections are distinct history | Existing close endpoint; no safe reopen endpoint or interval chain | Create, audited Head-only descriptive edit, periods, approved rate, close, roster/import. Descriptive evidence does not authorize automatic expiry, renewal or changed billing |

Evidence: `Domain/Entities/Facilities/Contract.cs`, `Stall.cs`, `StallOccupancy.cs`; `Infrastructure/Persistence/Configuration/ContractConfiguration.cs`; `Application/Command/Stalls/RenewStallContract`, `ToggleStallStatus`, `CreateStall`, `BulkImportStallholders`; `API/Controllers/Facilities/StallsController.cs`; `Client/Components/Pages/Shared/AddVendorModal.razor`; `Application/Common/Revenue/ObligationWorkflow.cs`; `Domain/Entities/Revenue/ObligationAccount.cs`. Paths above abbreviate the `EEMOCantilanSDS.*` project prefix.

Implementation-status correction (October 10): the KM matrix row above supersedes this document's initial missing-evidence snapshot. See [the October 9 verified checkpoint](../planning/OCCUPANCY_REGISTRY_PARITY_HANDOFF_20261009.md) for nullable metadata, local migration inspection and PostgreSQL verification. This records implementation evidence only; the contract-lifecycle approval gate and facility scope are unchanged, and no production migration status is asserted.

Existing facility renewal (`POST /api/stalls/{stallId}/renew`, relative to the controller's actual route) preserves the physical stall, adds a new Contract ID, and clamps outgoing `EndedOn` to the earlier of the day before commencement and original expiry. It can accept a changed occupant and optional rate; the UI/API therefore currently overload renewal and handover. Existing occupant-edit methods can also update the active term: they are correction behavior, not an approved successor-occupant design. Do not use them to overwrite an old occupant's liability history. Existing payor-link revocation on changed occupant is compatibility behavior, not authorization for name-based identity linking.

`StallOccupancy` reconstructs windows from retained terms, later starts and closure; `AnsweringForMonth` chooses one monthly owner. Facility billing already stops at the term's billable end and expiry projections already exist. The new clarification does not authorize copying that billing behavior into Kanmanggay, which currently has a continuous active account interval and no signed-term expiry.

The existing facility status reopen calls `Stall.Reopen` and `ExcuseClosurePeriodAsync`: absent daily records or eligible empty monthly records preserve a frozen span without overwriting recorded money. Creation/import paths also call `Reopen`; they require a separate route-by-route review before promising equivalent interval/audit guarantees. Existing status/renew endpoints inherit SuperAdmin/Admin controller authorization; do not assume they already implement the Head-only governance proposed for new account actions. No existing behavior or authorization was changed here.

## Proposed lifecycle and distinct operations — awaiting approval

Conceptually: Registered → Active → Expiring → Expired → Renewed or Closed. These are derived contract states, not proposed replacements for account lifecycle/payment status. A closed account may retain an unpaid balance; an active account may have a lapsed document. Define the consequence of expiry before introducing automation.

| Operation | Proposed meaning | Identity/history treatment |
|---|---|---|
| Renew signed contract | Same occupant receives an approved subsequent three-year term | Append a contract term linked to the same registration/occupancy where continuity is approved; retain preceding document, rate and dates. Does not inherently require a successor financial account |
| Register successor occupant | Physical space changes hands | Explicit new source-owned occupancy and term with independent holder/liability; preserve former occupancy. Reuse facility occupancy ownership rules; never infer identity from equal names |
| Reopen closed account | Resume after a genuine closed interval | Preserve the closed interval and original closure evidence. Kanmanggay may need a successor account/occupancy period, subject to the separate proposal below; renewal alone cannot erase the gap |
| Change approved amount | Effective-dated rental approval | Preserve the existing audited rate workflow. No new term or renewal is implied; do not reprice posted events |

For Kanmanggay, evaluate an additive term attached to the source registration: verified commencement, three-year duration, document reference, derived exact expiry, prior-term link, approval actor/date/evidence and explicit early termination. Store a separate renewal approval/effective date only where it is a distinct office fact. Existing effective-dated rate history should be reused, not copied into a competing rate engine. Do not claim a timeline from the current-rate DTO alone.

The earlier [successor-account reopening proposal](../planning/MOBILE_SPACE_SOURCE_FOLLOWUP_20261009.md) remains a proposal for discontinuous closed accounts, not a universal rule for renewal, occupant succession or rate changes. Its first-of-month reopening rule needs approval and must be reconciled with monthly liability ownership; it is not an accepted new contract rule. A continuous same-occupant renewal may append a term without changing financial identity. Reopening and renewal may both be required in one office case, but remain separately authorized acts.

## Compatibility and implementation gate

- NPM/NCC/TCC already have a reusable lease/term engine. Enforcement/defaults for new three-year terms, document approval evidence and distinguishing renewal from succession require approved UI/API rules. Do not create a duplicate lease subsystem.
- Kanmanggay needs additive persistence and explicit term/registration relationships before real expiry/renewal can be shown. A migration is likely for term records/approval/predecessor relationships; exact schema is not approved. A verified three-year record must not be manufactured from ActiveFrom or ContractRef alone.
- Existing records stay valid evidence, including variable historical terms and unknown/unsigned contracts. No blanket three-year conversion, reopening, automatic extension or expiry job. Backfill requires actual office documents and a separately reviewed reconciliation plan.
- APIs eventually need term read/history and Head-authorized approval/renewal operations with tenant scoping, concurrency, replay and audit. Source-native account IDs and canonical allocations remain stable. Mobile should consume resulting server eligibility/balances, never derive expiry or write a second monthly obligation.
- Billing effects at expiration/holdover, renewal gaps and midmonth succession must be explicitly approved. Preserve current assessments, immutable Collections/SRCs, settlement periods and effective rates. Approval of the term rule is not approval to alter any cash or billing history.

No new contract persistence, renewal, reopening, expiration automation, migration or financial behavior is implemented by this follow-up. Existing capabilities are documented, not reauthorized or changed.

## Outstanding MEEDO decisions

1. Does every renewal require a newly signed document, number and approval? Who authorizes it, and when does the next term commence?
2. Are early termination and temporary closure distinct? What dates, reasons and approving evidence are required?
3. Are Space Only/Extension arrangements still permitted for new Kanmanggay/commercial registrations, or only accepted legacy/temporary/incomplete-document exceptions?
4. Does expiry stop occupancy/billing, permit holdover, or require explicit closure? How are late renewals and gaps treated, particularly Kanmanggay versus existing facility rules?
5. May renewal change approved rent, and is there a separate effective-rate approval/document? How is a midmonth change allocated?
6. Do BBQ, ICE, Vegetable/Fruits, custom spaces or other sources have the same term? Fiesta/Araw remains an event source unless separately ruled otherwise.
7. Which existing records have verified commencement/term documents, and how should missing evidence be represented without inventing history?
