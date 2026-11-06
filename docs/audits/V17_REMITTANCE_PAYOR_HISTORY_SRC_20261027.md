# V17 payment display / SRC investigation — 2026-10-27

## User-reported evidence

- Live screenshot of Cantilan NCC Stall 24: payment month October 2026, ₱1.00 paid online, physical OR text `OR-2026-01`, Collection Activity explicitly states `Legacy payment` and `no SRC and no canonical correction`. Payor History displays the same paid ₱1.00 and OR.
- Remittance group view joins multiple `CollectionLine` classification labels verbatim (e.g., `Slaughterhouse · Slaughterhouse` repeated). This is presentation duplication; underlying individual lines, amounts, SRCs and remittance totals must remain intact.

## Current financial authority (read-only audit)

`OnlinePaymentSettlementService` already routes only `SettlementAuthority.Canonical` monthly PaymentRecords into `OnlineRentCanonicalPoster`, which atomically records a real canonical Collection/SRC with the paid gateway transaction and applies allocation under one idempotent operation. A Legacy PaymentRecord follows its prior settlement path and does not create a canonical Collection/SRC. The published ST-20 audit documents the required prospective review-and-activation process for new eligible rent months; nothing activates automatically. Existing paid or OR-bearing legacy months are specifically excluded. A physical OR is separate from the StallTrack SRC. No retrofit/backfill or financial record rewriting was authorized here.

## Changes in isolated branches

- EEMO `fix/v17-remittance-source-clarity-20261027`: both ordinary detail and **group submission** now use the pre-existing shared `RemittanceSourceLabel.For` formatter to de-duplicate same-classification names without changing any collection lines, SRC values, by-source amounts or remittance balances. Dedicated BUnit assertions cover both pages.
- Platform `fix/v17-payor-history-receipt-20261027`: each month/space is a keyboard-accessible clickable history item; opening it shows a mobile-friendly read-only **payment-record summary** with period, facility/space, real recorded OR or office-pending status, paid amount, assessed amount, balance, attribution, payment date and original daily entries. Escape/backdrop/Close dismiss the modal. The UI clearly states it is not a physical Official Receipt and never constructs an SRC. Existing API authorization and financial calculations remain unchanged.

## Evidence

- EEMO RemittanceWorkspaceTests (includes ordinary and grouped detail): **24 passed, zero failed**.
- Platform Payor Vitest: **76 passed, zero failed**, including two new History detail cases.
- Platform `nx build payor --skip-nx-cache`: **passed**.
- Legacy SRC activation and genuine PayMongo end-to-end flow: **not changed or retested** by these UI fixes; no production migrations, payment mutations, tenant-wide toggles or automatic deployment.

## Remaining separately governed financial task

To issue real SRCs for subsequent online rent collections on eligible months, use the existing ST-20 plan/review/attestation/activate API per tenant and facility/month, after confirming backup and restore, collector queue drainage, unambiguous settlement authority, zero legacy money/OR evidence and source policy. Review plan exclusions; never activate or relabel the pictured ₱1.00 already-paid legacy row. For Payor History to display canonical SRCs later, implement an authenticated, tenant- and stall-scoped read-only allocation-reference query rather than making up a reference from an OR or month.
