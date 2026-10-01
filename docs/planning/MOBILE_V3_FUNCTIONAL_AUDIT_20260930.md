# Mobile V3 Functional Audit (2026-09-30, completion pass)

Baselines: HEAD 82112944 (completion pass) and 11944ce6 (legacy workflow layout pass). Verified by reading code, unit and PostgreSQL integration tests, and building
`net10.0-android` Debug and Release. **No runtime
or visual review on Android was performed; the product owner will run it on a Windows machine.** A screen that renders in code is not marked production-ready here.

## Shell

Semantic `--mobile-*` tokens in `wwwroot/css/eemo.css`; light compact header (server business date, tenant seal/name); quiet
bottom nav (Menu, Records, Reports, Profile) with 44px targets and safe-area padding; light Login and native splash; sub-page
headers, banners and primary actions moved to the civic-blue/light tokens; heavy shadows and gradients removed from page CSS.
Legacy palette variables remain because unmigrated rules still reference them; they are not removed.

## Matrix

| Operation | Assigned | Capability | Screen | Read | Write | Offline | Instrument | Accountable document | Source authority | Production ready | Remaining gap |
|---|---|---|---|---|---|---|---|---|---|---|---|
| NPM daily / arrears / utilities | Facility menu | Facility available | `/market` | Server round + cache | Legacy NPM payment writers (unchanged) | Queue, OR | OR | OR (typed by collector, legacy) | Legacy | V3 field layout; `@code` unchanged except display name | Not device-reviewed |
| TCC / NCC / BBQ / Ice | Facility menu | Facility available | `/collect/{code}` | Server + cache | Legacy monthly writers | Queue, OR | OR | OR | Legacy | Shared V3 layout; obligation / paid / remaining from server; `@code` unchanged | Not device-reviewed |
| WCF | Operation | Ready gates collection | Market stall sheet | Server outstanding quote | Governed WCF (`WcfCollection`) | Durable issue-then-sync; same ClientOperationId; document never reusable | CT | Assigned CT | Canonical after cutover | Code audited and tested | Not device-reviewed |
| ECF | Not a Mobile writer | n/a | Context only | none | none | n/a | n/a | n/a | n/a | n/a | No Mobile writer (not invented) |
| Fish/Meat weighing | Within NPM | with NPM | `/market` | server rates | legacy | queue | OR | OR | Legacy | untouched | not device-reviewed |
| Fish/Meat Vendor Fee | Not exposed | none | none | none | **none** | none | n/a | n/a | Web | **No** | No Mobile writer (gap) |
| Slaughterhouse | Facility menu | available | `/slaughter` | Collection + `ApprovedAnimals` | legacy SLH writer, server enforces approved animals | queue, OR | OR | OR | Legacy | Approved-animal select, historical custom rows read-only | Not device-reviewed; no packages/add-ons (no backend) |
| Transportation | Operation | Ready gates | `/operation/TRANSPORTATION` | Terms with vehicle classes | Governed | Durable issue; `VehicleClassCode` required | CT | Assigned CT | Canonical | Code and tests | Activation is server-side (`PendingCutover` shows "Pending activation") |
| Market Fees / Landing / Transfer Large Cattle / Vegetable-Fruit | Operation | Ready gates | `/operation/{code}` | Terms, documents | Governed | Durable issue | Per server terms | Assigned | Canonical | Code and tests | Not device-reviewed |
| Tabo / TPM | Facility menu | available | `/taboan` | server | legacy | queue, OR | OR | OR | Legacy | V3 layout; "Tabo vendor fee" wording; `@code` unchanged except display name | Not device-reviewed |
| Kanmanggay | Not exposed | none | none | none | **none** | none | n/a | n/a | Web | **No** | No Mobile writer (gap) |
| Fiesta / Araw | Not exposed | none | none | none | **none** | none | n/a | n/a | Web | **No** | No Mobile writer (gap) |
| Records | n/a | n/a | `/records` | Legacy feed + **new** collector-only canonical register (grouped per document) | none | Local queue kept separate (Pending / Failed / Needs review) | n/a | n/a | Server | Code and unit/integration tests | Canonical register is per collector and period; not device-reviewed |
| Reports | n/a | n/a | `/reports` | **Position tab** (server) + collector report = legacy facility sources + posted canonical Collections, once (`CollectorReportComposer`) | none | cached read | "Waiting to sync" shown apart, never in totals | walk-up CT never a payee | Server | Unit + PostgreSQL integration | Custom period not built |
| Profile | n/a | n/a | `/profile` | identity, facilities, operations status, sync, position link | notifications toggle (device-local) | n/a | n/a | n/a | n/a | Code | Not device-reviewed |
| Menu | n/a | Ready only opens | `/menu` | capabilities + queue | none | n/a | n/a | n/a | n/a | Grouping unit-tested | Not device-reviewed |
| Offline / sync | n/a | n/a | n/a | n/a | n/a | Store, sync, ClientOperationId, OwnerKey, retry, reconciliation unchanged | n/a | never re-available | n/a | Existing plus new tests | none new |
| App update / FCM | n/a | n/a | Menu banner | n/a | n/a | n/a | n/a | n/a | n/a | Untouched | Version not changed |

## WCF offline sequence (audited)

1. The screen offers only server quotes with outstanding above zero and a Ready WCF capability; the amount must not exceed the server outstanding.
2. An assigned Cash Ticket is selected from the server list, minus any ticket already in the local queue (rebuilt from the queue on each load, so it survives a restart).
3. One `ClientOperationId` is created per capture.
4. `EnqueueIssuedDocumentAsync` durably saves the ticket and operation before the screen reports success; if storage fails, the screen tells the collector not to issue the ticket.
5. Sync reuses the stored operation id; `ReconciliationRequired` stays visible in Records and on the Menu ("needs office review").
6. The same document cannot be enqueued twice (tested); the wire type carries no meter, cubic-meter or rate member (tested).

## Tests added this pass

Collector position (own identity, cross-tenant, admin/collector refusals), collector register, Today's Work capability rules,
operation record grouping, Transportation store validation, WCF reuse and wire shape.

## Collector reporting reconciliation (2026-10-01)

Runtime case: Landing/Berthing ₱100 on CT000001, business date Oct 1 2026, posted (PostingOperation status Posted, one
Collection, one `LANDING_BERTHING` line, no correction, no remittance). Reports showed ₱0 on By Month, Per Payee and Summary.

| Tab | Endpoint | Legacy coverage | Canonical coverage | Exactly once | Before |
|---|---|---|---|---|---|
| Position | `GET api/Mobile/position` (`RemittanceWorkflow.GetMyPositionAsync`) | stated apart as "Recorded before activation" | all posted Collections, net of corrections | yes | correct |
| By Month / By Day | `GET api/Mobile/report` | facility sources of assigned facilities | posted Collections by business date | yes (authority map) | facility-only |
| Per Payee | same | stall/vendor accounts | registered Payors by explicit link; walk-up CT as one aggregate | yes | facility-only |
| Summary | same | same | "Posted collections" block | yes | facility-only |

Authority: canonical facts are the Position's facts, so Position and the report cannot state two canonical totals. Legacy
PaymentRecord money is skipped once its row is canonical. Business date: `Collection.BusinessDate`, whole month, inclusive.
"Payor accounts" (was "Payees") counts facility accounts in scope plus registered Payors; a walk-up ticket never adds one.
