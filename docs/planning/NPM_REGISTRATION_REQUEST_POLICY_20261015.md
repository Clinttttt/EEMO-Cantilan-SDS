# NPM registration requests: confirmed nonfinancial boundary

Status: task-specific office ruling confirmed on 2026-10-15; implementation handoff, not authority to deploy or apply migrations.

The owner answered: **“Registration request only; no cash before approval”**, and clarified that the current collection identity is SRC, not CT. This supersedes the task's tentative preapproval cash-intake proposal. See [implementation handoff](MOBILE_REPORTS_NPM_REGISTRATION_REUSE_20261015.md).

## Decisions and consequences

| Question | Confirmed implementation boundary |
| --- | --- |
| May a collector accept NPM rent before tenancy approval? | No. The request form records proposed tenancy evidence only. |
| What receipt is issued for a request? | None. No Collection, SRC or accountable-form allocation is created. |
| Who holds pending cash? | There is no authorized pending-cash intake or custody state in this workflow. |
| Can a request be rejected or returned? | Yes, with a recorded Head reason. No cash reversal or refund is required or implemented because no cash may be accepted. |
| Does approving a request authorize tenancy by itself? | No. The Head must first create/verify the actual NPM occupancy and explicit Fish/Meat association through the normal authorized workflow. Review then references that verified association. |
| Which rental basis/rate is used? | Normal NPM registration and subsequent source-specific quote/writer remain authoritative. Proposed stall/section and vendor classification do not establish a rate. |
| How does later collection enter remittance? | Through the existing canonical NPM collection operation only, after source eligibility and successful posting. A registration decision contributes zero to collected, unremitted and remitted totals. |

There is deliberately no “Approve & Collect” action and no request amount, proposed payment period, receipt number or payment-posting endpoint. The ordinary posted Direct Collection Review retains its existing meaning.

## Identity and source ownership

Fish/Meat registration remains independent of NPM tenancy. Selection reuses the stable vendor registration ID and recorded name; Fish/Meat classification suggests a section for office confirmation. Dates, stall, area, rental basis, rates and contract facts remain separate NPM evidence. Equal names never establish a relationship. Existing association confirmation/correction history is retained.

## Lifecycle

Draft is local form state. Submission creates `Submitted` (pending Head review). The Head may record `Approved`, `Returned`, or `Rejected` once, using an expected version and decision operation ID. Approval requires a currently applicable verified association. A returned proposal is not overwritten: a subsequent submission has a new operation ID and references the returned record. Rejected and pending requests create no tenancy or financial record.

## Receipt terminology

New request UI says **SRC** and explicitly states none is issued. This ruling does not authorize rewriting historical OR/CT evidence, instrument enums, allocation history or existing receipt compatibility paths. A canonical SRC is assigned only by the established Collection posting service.

## Future changes

Any proposal to accept money before approval requires a separate confirmed policy covering lawful receipt evidence, accountable custody, rejection/refunds, reconciliation, remittance, and an auditable posting design. No such authority is assumed here.
