# SRC Phase 2 — Mobile OR-entry audit and cutover classification (2026-10-04)

Status: audit result. Governing decision: IA-062 (SRC is the digital identity of a canonical Collection; the physical OR/CT serial is not a StallTrack collection input). Class **A** = already canonical writer, **B** = legacy writer with an approved canonical equivalent, **C** = prospectively cuttable with already-approved classification/policy, **D** = lacks approved backend/domain rules (no safe cutover without new decisions).

> **Superseded in part.** This table is the original Phase 2 snapshot. The current state of every path (including Tabo and Slaughterhouse, now canonical after Head enablement) is the **Phase 2.2 final-state table at the bottom of this document**.

| Mobile screen / operation | Authority (`CollectionSourceAuthorityMap`) | Current writer | OR dependency | Class | What a safe cutover needs |
|---|---|---|---|---|---|
| Governed services (`OperationCollection`: Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit, Transportation) | `GovernedService` = CanonicalAlways | `GovernedServiceWorkflow` | none (since IA-062) | **A** | done |
| WCF (`WaterCollection`, Market WCF section) | `UtilityBill` water part, canonical prospectively (IA-054) | `WcfCollectionWorkflow` | none (since IA-062) | **A** | done |
| Market daily (NPM) — `Market.razor` daily sheet | `DailyCollection` = **LegacyOnly** | `RecordDailyCollectionCommand` (`DailyCollection`) | typed `ORNumber` | **D** | a canonical source kind + approved classification/amount basis for NPM daily fish/meat fees and a Mobile canonical writer; the authority map must change from LegacyOnly through a controlled prospective cutover |
| Market Electricity (typed `ElecORNumber`) | `UtilityBill` electricity part, Legacy until row cutover | legacy `RecordUtilityPayment` path | typed `ElecORNumber` | **D** | ECF has no Mobile canonical writer by design (Web Composer only); needs a Mobile ECF writer over `EcfCollectionWorkflow` plus per-row `SettlementCutoverWorkflow` activation with the device/writer attestations |
| Monthly rent — `MonthlyCollection.razor` | `PaymentRecord` = CanonicalAfterRowCutover; rows are Legacy until cut over | `RecordPaymentCommand` (cumulative legacy payload) | typed `ORNumber` | **D** | a Mobile canonical rent writer over `MonthlyRentCollectionSourceAdapter` (it exists for the Web Composer) and a controlled per-row cutover; the Mobile cumulative legacy payload still has no late-issue reconciliation adapter |
| Taboan (TPM vendors) — `Taboan.razor` | `TpmAttendance` = **LegacyOnly** | `AddVendorToMarketDayCommand` | typed `ORNumber` | **D** | canonical source + approved classification/rate/instrument for Tabo fees |
| Terminal (TRM trips) — `Terminal.razor` | `TrmTrip` = **LegacyOnly** | `RecordTripCommand` | typed `ORNumber` | **D** | same as above for terminal fees |
| Slaughter — `Slaughter.razor` | `SlaughterTransaction` = **LegacyOnly** | `RecordSlaughterCommand` | typed `ORNumber` (required) | **D** | canonical source + approved animal rates/add-on classification (add-ons remain an open decision) |
| `Record.razor`, `Report.razor` | display only | — | show legacy OR evidence | n/a | truthful for legacy rows; canonical rows show SRC |

**Result:** no remaining OR-entry screen is class A/B/C, so none was altered in this phase; hiding the textbox while the legacy command still needs it would fabricate a collection model. Every D row needs a business/domain decision and an attested controlled cutover, neither of which may be done implicitly. Historical OR evidence stays intact.

## Done in this phase
- `SettlementCutoverWorkflow` no longer treats physical accountable-document inventory as a gate: reconciliation-required documents, assigned-document custody disagreement and `AccountableDocumentInventoryReconciled` are informational warnings; custody no longer feeds the readiness fingerprint. Financial gates are unchanged (unresolved posting operations, online payments, writer quiescence, drained device queues, device/collector attestations, reporting-path verification).
- Collection Activity accepts an exact, case-insensitive `reference` (`SRC-YYYY-NNNNNN`) and returns that canonical Collection on its own business date, tenant-scoped; the page jumps to that day. Without a reference the day query is unchanged. Legacy rows never match.

---

## Phase 2.1 reconciliation with the confirmed decision registry (2026-10-04)

The table above is an **implementation audit**; it does not override IA-045/049/050/051/053/054/062. Re-reading those decisions, the original "Class D" label was **too conservative**: it treated "no canonical Mobile writer exists yet" as "business decision missing". The correct split is policy gap vs implementation gap vs cutover gap.

| Mobile path | Revised category | Basis | Result in Phase 2.1 |
|---|---|---|---|
| Governed services, WCF | **Already canonical** | IA-044…IA-054 | unchanged |
| Transportation / TRM (`Terminal.razor`) | **Cutover already built; Mobile routing gap** | IA-051: Transportation is CT at approved vehicle-class rates; enabling the service is the boundary and the legacy trip writer is already closed server-side (`TransportationCollectionAuthority`) | **Built:** the Mobile Menu routes PerTrip facilities to the governed `/operation/TRANSPORTATION` page once the service is enabled; before that the legacy Terminal screen remains. History is not touched and nothing is backfilled. |
| ECF / Electricity (`Market.razor`) | **Implementation gap + per-row cutover** | IA-050/053: ECF = OR policy, direct approved amount, Web/Mobile converge on one source | **Built:** `CollectionComposerWorkflow.PostMobileEcfAsync` (reuses the Web composer's facts, policy, snapshot and projection), `OfflineOperationKind.EcfCollection`, SRC in the sync result. The Electricity sheet drops the OR textbox for a source whose Electricity authority is Canonical; Legacy-authority bills keep the typed-OR path until their controlled activation through `SettlementCutoverWorkflow`. |
| Monthly rent (`MonthlyCollection.razor`) | **Implementation gap + per-row cutover** | IA-051: prospective source-by-source authority; `MonthlyRentCollectionSourceAdapter` + `PaymentRecord = CanonicalAfterRowCutover` | **Built:** `PostMobileRentAsync` over the same adapter, `OfflineOperationKind.RentCollection`. A canonical rent row is collected with no OR number and returns an SRC; Legacy rows keep the legacy sheet until activated. |
| Taboan / TPM | **Implementation gap (policy confirmed)** | IA-045 Tabo = OR, `TABO` classification, fee from effective `FeeRateKey.TpmVendorDay` | **Not built.** The Transportation precedent shows the cheap pattern (a governed canonical operation + closing the legacy writer at enablement, no reader changes), but it needs a new governed catalog entry, Head setup, a Taboan rewrite that keeps the vendor/attendance list, and report-row mapping. Recorded as the next slice, not a business-decision gap. |
| Slaughterhouse | **Implementation gap with one genuine open sub-rule** | IA-050: approved definitions/rates only; add-ons/packages unresolved | **Not built.** A bounded animal x approved-rate canonical writer is possible, but the Head-managed approved-rate definitions and the add-on classification are not complete; leave legacy until both exist. |
| NPM daily (`DailyCollection`) | **Genuinely specialized remaining migration** | RentGoal/PureDays, closures, absence, month-end adjustment, `NpmMonthSettlementService` | **Left legacy.** No separate canonical adapter can capture its money without moving or duplicating NPM arithmetic; it is never reclassified as Fish/Meat Vendor Fee (IA-050). |
| Fish/Meat Vendor Fee | **Distinct future source** | IA-050 separate obligation | Obligation-account infrastructure exists (Web); no Mobile writer. Not hijacked from DailyCollection. |
| `Record.razor`, `Report.razor` | **Legacy display-only** | | unchanged; canonical rows show SRC |

### Authority and counting
`CollectionSourceAuthorityMap` is unchanged. ECF and rent use the existing row authority (`UtilityBill` electricity / `PaymentRecord` = CanonicalAfterRowCutover): before a row's activation legacy counts, after it only the canonical Collection counts, and the Mobile canonical writers refuse a Legacy-authority row so one payment can never create two money events. Integration tests prove, for ECF: no serial needed, SRC returned, idempotent replay (same CollectionId and SRC), one Collection, legacy OR evidence untouched, no form consumed, Collection Activity lists one canonical row with the SRC, collector facts/remittance eligibility see the same Collection, and legacy / stale / over-amount / non-collector attempts are refused without writing; and for rent: no serial, SRC, replay, allocation on the PaymentRecord with the projected partial, legacy row refused.

---

## Phase 2.2 — financial proof for ECF/rent, and canonical Tabo and Slaughterhouse (2026-10-04)

### Final state of every Mobile collection path

| Path | State | How a Collector's money is written |
|---|---|---|
| Governed services (Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit) | **Canonical** | `GovernedServiceWorkflow`; Collection + SRC, no serial |
| WCF | **Canonical** | `WcfCollectionWorkflow` |
| Transportation / Parking | **Canonical after Head enablement** (legacy trip writer closed from that date) | governed `TRANSPORTATION`; Menu routes to it |
| ECF (Electricity) | **Canonical for rows already activated**; legacy for pre-cutover rows | `PostMobileEcfAsync`; SRC, no serial |
| Monthly rent | **Canonical for rows already activated**; legacy for pre-cutover rows | `PostMobileRentAsync`; SRC, no serial |
| **Tabo (TPM)** | **Canonical after Head enablement of the `TABO` service** (OR policy, effective-dated); legacy before and for historical CT/OR rows | `FeeScheduleCollectionWorkflow`; SRC, no typed OR |
| **Slaughterhouse (SLH)** | **Canonical after Head enablement of the `SLAUGHTERHOUSE` service** (current per-head transaction rules); legacy before and for historical rows | `FeeScheduleCollectionWorkflow`; SRC per animal line, no typed OR |
| NPM daily (`DailyCollection`) | **Legacy** (not migrated) | `RecordDailyCollectionCommand` |
| Fish/Meat Vendor Fee | **Not built** | — |
| Slaughterhouse packages / add-ons | **Not built** (none are active in the current rules) | — |

### Phase 2.1 closure (financial proof, no new behaviour)
For one canonical Mobile ECF collection and one canonical Mobile rent collection, PostgreSQL integration tests prove: counted exactly once in the official Monthly Income row (legacy projection excluded), appears once in the Collector Report under its SRC with no legacy duplicate, appears once in the Collection Activity and the Remittance position (collected = amount, remitted = 0, unremitted = amount); a remittance record moves only remitted/unremitted, never income, and creates no Collection or line. The Mobile.Core queue test proves the real `PendingOperationStore` + `MobileSyncService` keep a queued canonical operation across a failed attempt and reconcile with the server's SRC on retry under the same `ClientOperationId` (never a second identity).

### Tabo and Slaughterhouse: design
- **Boundary (IA-051 pattern, as Transportation).** `TABO` and `SLAUGHTERHOUSE` are added to `GovernedServiceCatalog` (Basis = DirectApprovedAmount). The Head's governed setting (IsEnabled + MobileEnabled, effective-dated) is only the prospective switch; it carries no amount. They are deliberately *not* in `CollectorOperationCodes.IsSupported` (they are facility-assigned, TPM / SLH), so they do not appear as assignable operations. The generic governed post refuses them (a collector-typed amount can never reach them).
- **Amount = the existing rules, never typed.** Tabo: market weekday from `ITpmMarketDayProvider`, registry vendor matched by name (as the existing handler), fee `FeeRateKey.TpmVendorDay` from `IFeeRateResolver`, one vendor per market day (an earlier legacy attendance or canonical line refuses). Slaughterhouse: `SlaughterRateKeys` + approved `SlaughterAnimalRates` registry, and the existing `SlaughterTransaction.Create*` calculation is run on an unsaved instance (no second algorithm, nothing stored). The collector-confirmed amount must equal the computed amount; otherwise the post is refused and a durable `Rejected` PostingOperation is recorded.
- **One writer, no twin.** The workflow posts only the canonical Collection (`SourceKind.GovernedService`, `SourceId = service.Id`, snapshot carries vendor/animal/heads/rate and the instrument policy). It writes **no** `TpmAttendance` / `SlaughterTransaction`, so a real payment is never both a legacy record and a Collection. A new Tabo vendor is registered in the vendor registry only when the post succeeds (a rejected post leaves no vendor).
- **Legacy writers closed for Collectors from the enablement date.** `AddVendorToMarketDayCommandHandler` and `RecordSlaughterCommandHandler` refuse a *Collector* actor for a date on which the service is enabled (`GovernedCanonicalAuthority`). History stays readable and unchanged; Admin/Web entry is unchanged.
- **Instrument from policy.** The line freezes the effective `RevenueClassificationPolicy.PermittedInstrumentType` (OR for both under the current policy; Tabo's historical CT rows stay as recorded). No accountable document is created, consumed or required; the SRC is **not** proof a physical form was used.
- **Mobile.** The Menu item carries `CanonicalCollection`. `Taboan.razor` / `Slaughter.razor` hide the OR box for the canonical path, save under a `ClientOperationId` on the device ("Waiting to sync"), sync, and show the SRC; legacy screens are unchanged before enablement. New queue kind `OfflineOperationKind.FeeScheduleCollection = 11` (shape-checked in `PendingOperationStore`).
- **Slaughterhouse granularity.** The existing "transaction" is the per-animal-line `SlaughterTransaction` (the owner+OR grouping is only a UI grouping). The canonical path therefore posts **one Collection / SRC per animal line**, preserving the current per-transaction semantics. Combining several lines under one SRC would change no total and is a deferred product choice, not a money-rule ambiguity.

### Reporting and exactly-once (proven on PostgreSQL)
For one Tabo and one Slaughterhouse collection: official Monthly Income row `TABO` / `SLAUGHTERHOUSE` counts it once; the Collector Report lists it once as a canonical operation collection under its SRC with no legacy line; Collection Activity lists one canonical event; the Remittance position moves only collected/remitted/unremitted and a remittance creates no Collection. Idempotent replay returns the same Collection and SRC; an idempotency conflict (same id, different intent), cross-tenant collector, non-collector role, unassigned collector, not-enabled service, wrong typed amount, non-market day, duplicate vendor-day, unstated rate, unapproved animal and zero heads are all refused with nothing written.

### Known limits (recorded, not hidden)
- No Head UI yet to enable `TABO` / `SLAUGHTERHOUSE`; use the existing `PUT api/governed-services/{code}` with Basis = DirectApprovedAmount, IsEnabled and MobileEnabled.
- A device whose cached Menu pre-dates enablement may still open the legacy sheet; the server's closure refuses it with a clear message (nothing is written). Legacy operations already queued on a device before enablement and synced after it are refused for the same reason and appear as Rejected for review - never silently doubled.
- Web/Mobile operational lists (Taboan attendance, Slaughter transactions) are legacy lists and do not carry canonical Collections; Mobile shows its own recorded SRC cards, and the office sees them in Collection Activity / Collections Register.
- Admin/Web legacy writers remain open for Tabo and Slaughterhouse (office entry, unchanged).

### Not built / still legacy
Fish/Meat Vendor Fee Mobile; NPM daily migration; Slaughterhouse packages/add-ons (none active); Head setup UI for the two new switches; backfill of any historical row (none attempted).

> Superseded by Phase 2.3 below for: Head setup UI (built), Fish/Meat Vendor Fee Mobile (built). NPM daily remains legacy (blocker below).

---

## Phase 2.3 — presentation closure (2026-10-04)

### Built
- **Head activation, no API client.** `FacilityCanonicalCollection` sits in the Tabo-an and Slaughterhouse **Configure** drawers (Facility Configuration). One switch saves "enabled for new transactions" and "Collector Mobile" together, so the half-enabled state cannot be saved from there. No Revenue Setup screen was added.
- **Half-enabled governed state.** The governed workspace opens the editor enabled (it used to open a Disabled service unchecked and save it Disabled again), offers a one-click "Enable new transactions" for a Disabled service, and says "Allowed, but new transactions are not enabled" instead of a bare "Enabled" beside Disabled. Local data root cause: Vegetable/Fruit had a 2026-10-04 setting with IsEnabled = false, MobileEnabled = true.
- **Policy-version duplicate.** Enabling never needs a new policy row. The Schedule-policy dialog now defaults to the day after the latest version, so the Head is not handed a date the history already holds.
- **Vegetable / Fruit.** Wording is Monthly rental (OR) / Daily transaction (CT) on Web and Mobile; Mobile mode tiles and the facts card show the policy instrument. The mode resolves the instrument (IA-046); the collector never picks it. Both modes post one Collection with an SRC (proved on PostgreSQL and in the live Mobile app).
- **Market Fee definitions on Mobile.** Mobile already read active fee options live (stable `FeeOptionId`; read-through cache only as the offline fallback). Local root cause of "not visible": Market Fees was on the **Direct approved amount** rule, and fee types are offered to collectors only under **By approved fee type**. The workspace now says so and offers "Collect by fee type". Verified live: Head adds Comfort Room (Terminal / Public Market) in Web, Mobile lists both, a post references the option id, retiring removes it from new collections, history stays readable.
- **Fish / Meat Vendor Fee Mobile.** The authoritative domain is the monthly **obligation account** on an NPM Fish/Meat stall (IA-050), not a per-kilo weighing. `PostMobileObligationAsync` reuses `ObligationCollectionSource` (same facts, Official Receipt policy, snapshot as Web); the collector picks account and period, the amount is capped at the period balance, no serial, SRC returned, idempotent, offline-queued (`OfflineOperationKind.ObligationCollection`). Distinct from NPM daily, Tabo, Market Fees and Weight and Measure.
- **Accountable Forms.** Physical-book registration moved behind a collapsed "Record a physical book (back-office)" disclosure; the page is "Physical form history". Nothing is deleted; OR/CT labels stay; collection never depends on a registered book. Wording states that the SRC is neither an OR nor a CT.
- **Mobile display fixes** from the live run: day figures on the Tabo and Slaughter screens now include recorded canonical lines; SRC badges wrap; instrument shown on governed operations.

### Blocker: NPM daily canonical migration (not built)
Stopped for this sub-flow only, because continuing would mean guessing the transaction boundary and classification:
- `DailyCollection` is a mutable per-day status row (paid / unpaid / absent, re-mark, un-mark), not an append-only money event. Canonical Collections are append-only; a canonical NPM writer must define how mark-unpaid/absent and corrections map to a Collection correction.
- Its income is the **stall-rent row (`RENT_NPM`, `PERMANENT_STALL_RENT`)**; the day fee is a different cadence of the same rent source. NPM month settlement (rent goal / pure days, arrears, earned-through) reads `DailyCollection`. Whether a canonical day is an allocation against the month's `PaymentRecord` or a new source is an accounting decision, as is the classification and rate of the fish/meat weighing add-on on the same row.
- Needed from the Head/Core Brain: (1) the canonical source and classification for a daily stall fee, (2) how a day is corrected or voided, (3) whether month settlement reads canonical days. Until then the NPM daily writer stays legacy and unchanged.

### Known limits
- Vegetable / Fruit has one approved ceiling (Direct approved amount), not a separate configured amount per mode, and the repository states no monthly rate. The collector enters the amount within the ceiling. A per-mode fixed amount needs the Head to state the rates first.
- Market Fees offers fee types only under the By approved fee type rule (now explained in the UI).
- Fish/Meat dues list loads online; offline shows the last synchronized list and the server revalidates on sync.
