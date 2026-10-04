# SRC Phase 2 — Mobile OR-entry audit and cutover classification (2026-10-04)

Status: audit result. Governing decision: IA-062 (SRC is the digital identity of a canonical Collection; the physical OR/CT serial is not a StallTrack collection input). Class **A** = already canonical writer, **B** = legacy writer with an approved canonical equivalent, **C** = prospectively cuttable with already-approved classification/policy, **D** = lacks approved backend/domain rules (no safe cutover without new decisions).

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
