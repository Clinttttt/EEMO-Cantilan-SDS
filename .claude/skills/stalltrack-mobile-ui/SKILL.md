---
name: stalltrack-mobile-ui
description: Use before changing the StallTrack Collector Mobile (MAUI Blazor Hybrid) UI, navigation, capability handling or offline queue. Keeps Mobile a client of server-owned business rules.
---

# StallTrack Collector Mobile UI

Mobile is a collector client. It never decides money, classification, rates, instruments or remittance.

## Boundaries
- Owns `EEMOCantilanSDS.Mobile/**` and `EEMOCantilanSDS.Mobile.Core/**`. Backend contracts only change for an already-approved capability, and the change is small.
- Assignment != collectibility. Only a capability with status Ready may collect; every other status shows one concise mapped sentence.
- Amounts come from the server (terms, quote, approved animals, vehicle-class rates). No typed rate, animal name or manual instrument.
- Tenant name, seal and office come from `Session.Branding*`. Never hard-code a municipality, office acronym or peso figure.
- Use the server business date (`Session.Menu.Today`), not the device clock, for what a collection is recorded against.

## Offline safety (never weaken)
- Queue through `PendingOperationStore` / `MobileSyncService` with `ClientOperationId` and `OwnerKey`.
- A physically issued OR/CT is enqueued with `EnqueueIssuedDocumentAsync` BEFORE the UI reports it captured, and never returns to the available list.
- Reconciliation-required and failed items stay visible until resolved; the local list must not duplicate a synced server row.
- Keep app version and FCM registration behavior unchanged.

## Separations to keep
Operational source, obligation, collection, accountable document, revenue classification and report line are different things. Fish/Meat weighing, Vendor Fee and Stall Rental stay separate. Ticket counts are never pesos. Mobile cannot record or void a remittance.

## Visual
Light V3 surface, DM Sans only, civic-blue active accent, registers and dividers over card mosaics, touch targets of at least 44px, safe-area padding, 360-430px width.

## Verify
Mobile has no bUnit harness: cover logic in `EEMOCantilanSDS.Testing/Mobile`, build `net10.0-android` Debug, and say plainly what was not run on a device.
