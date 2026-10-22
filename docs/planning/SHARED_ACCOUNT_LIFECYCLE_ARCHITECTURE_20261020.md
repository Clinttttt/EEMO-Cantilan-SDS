# Shared Account Lifecycle — Architecture Assessment and Phased Plan (2026-10-20)

**Status:** Revision 2 (2026-10-20, after CORE BRAIN V9 review). Phase 1 (read-only foundation) is IMPLEMENTED in source; Phases 2+ remain PROPOSALS. No schema, migration, database, service or deployment change has been made. Anything marked **Pending Approval** is an unresolved MEEDO decision, not a confirmed rule.

Evidence read: `ObligationAccount`, `ObligationWorkflow`, `SpaceAccountNumbering`, `AppDbContext.BeginSpaceAccountTransactionAsync`, `ObligationConfiguration`, `Stall`, `Contract`, `StallOccupancy`, `ToggleStallStatusCommandHandler`, `SoftDeleteStallCommandHandler`, `StallsController`, `ClosedAccounts.razor`, `ObligationWorkspace.razor`, `DECISION_REGISTRY` (IA-006, IA-009, IA-068/ADR-007, IA-069), `MEEDO_CONTRACT_CLARIFICATION_20261009`, `OCCUPANCY_REGISTRY_PARITY_HANDOFF_20261009`, `MOBILE_SPACE_SOURCE_FOLLOWUP_20261009`, `KANMANGGAY_RENTAL_PARITY_HANDOFF_20261009`, `ObligationAccountTests` (30 PostgreSQL tests).

## 0. Corrections from CORE BRAIN V9 and the Phase 1 audit (verified against the code, 2026-10-20)

1. **Permissions are not uniformly SuperAdmin-only.** `StallsController` is `[Authorize(Roles = "SuperAdmin,Admin")]` at class level and no handler adds a role check, so **Admin or SuperAdmin** may Close/Resume (`PATCH {id}/status`), Renew (`POST {id}/renew`) and `assign-past-occupant`. Only `SoftDelete` (Remove) is SuperAdmin. For space accounts, `ObligationsController` close/create/holder/rate/import are SuperAdmin; reads are Admin or SuperAdmin. Phase 1 reports exactly these roles and a test pins them; no authorization change is proposed or made.
2. **Preview-version checks and durable idempotent execution do not exist on the Stall commands.** `ToggleStallStatus`, `RenewStallContract`, `AssignPastOccupantStall` and `SoftDeleteStall` take no operation id and no version; a repeated call runs again (e.g. `Close` on an already closed stall resets `ClosedAt`; `Renew` appends another `Contract`). Earlier wording that implied these guarantees is withdrawn: they are *target-state requirements for any new mutation*, and Phase 1 introduces **no mutation endpoint and no write wrapper**.
3. **Several Stall guards are advisory only.** The commands enforce: month-evidence activation (Close/Resume), closed market section (Resume), "closed or expired only" (Remove), SuperAdmin (Remove). They do **not** refuse Close on a closed stall, Resume on a stall that is not closed, Renew on any state, or Reassign on any state. The lifecycle contract therefore carries `Enforced` per action, so a screen never claims the server would stop a direct call it will not.
4. **Stall "Reassign" is not a handover.** `assign-past-occupant` places a returning payor in a *new* stall copied from the past occupancy; a handover of the same physical stall is recorded by **Renew with a different occupant** (which also revokes the outgoing occupant's payor-stall links). Both are documented as separate acts.
5. **Monthly Space Rentals need explicit billing-month ownership.** A monthly period is indivisible (one `ObligationPeriod` per account per month). Stalls resolve a handover month by "latest occupancy within the month wins" (`StallOccupancy.AnsweringForMonth`). No equivalent rule is approved for Space Rentals; until MEEDO decides it, successor occupancies and number reuse stay unimplemented (**Pending Approval**).
6. **Space renewal, holdover/late-renewal billing, handover months and removal with outstanding debt are Pending Approval.** None of the policies in §2.2 is an official ruling.
7. **IA-006 stands:** Closed Accounts is a reporting surface. It already mutates for stalls (Resume, Renew, Renew eligible, Assign new stall, Remove); that is a recorded exception to relocate in the UI phase, not a precedent to extend to Space Rentals.
8. **IA-009 / IA-068 / IA-069 boundaries are preserved:** holder identity is explicit and never inferred from names; the lifecycle never creates a Collection, SRC, rate or period; closed predecessors with balances remain Arrears and a successor never inherits a predecessor's debt.

## 0.1 Phase 1 as implemented (source only)

| Piece | Location |
|---|---|
| Contracts: `LifecycleSource` (Stall/SpaceRental/EventLot), `LifecycleAction` (View/Close/Resume/Renew/Reassign/Remove), `LifecycleState`, `LifecycleReason` codes, `AllowedAction` (with `RequiredRole`, `Enforced`), `LifecycleHistoryEntry`, `AccountLifecycleDto`, `IAccountLifecycle`, `AccountLifecycleResolver` | `Application/Common/Lifecycle/LifecycleContracts.cs` |
| Server-authoritative eligibility (pure, DB-free): `LifecycleRules.ForStall`, `ForSpace` | `Application/Common/Lifecycle/LifecycleRules.cs` |
| Read-only adapters: `StallLifecycle` (wraps the existing Stall/Contract data, section closure and month-evidence checks), `SpaceAccountLifecycle` (Kanmanggay, Vegetable/Fruit, Fiesta/Araw) | `Application/Common/Lifecycle/AccountLifecycleAdapters.cs` |
| Query + handler (auth, role, tenant claim, not-found without cross-tenant disclosure) | `Application/Queries/Accounts/GetAccountLifecycle/` |
| Endpoint `GET api/AccountLifecycle/{source}/{accountId}` (Admin/SuperAdmin, **GET only**) | `Api/Controllers/Revenue/AccountLifecycleController.cs` |
| Tests | `Testing/Application/Lifecycle/AccountLifecycleTests.cs` (unit, incl. permission characterization), `IntegrationTests/AccountLifecycleTests.cs` (PostgreSQL, real tenant filters) |

**Eligibility reviewed against the intended workflows (final stabilization):** Renew is advertised only for an expired term on an account that is still open (what Closed Accounts offers, and the endpoint's only caller); a running term reports `TermStillRunning` and a closed account `AccountClosed`, both with `Enforced = false` because `RenewStallContract` would still accept an early renewal or a handover. Reassign is advertised only for a past occupancy of a stall somebody else now holds (`NotReLet` otherwise, `Enforced = false`). Close/Resume/Remove keep their enforced guards (month evidence, closed section, closed-or-expired, Head only).

**Closed Accounts boundary fixed (2026-10-20):** the as-of/period reader treated an occupancy's `End` (the last day held, inclusive) as "not yet ended" on that day, so a term terminated exactly on the cutoff vanished from the register. On a past as-of date an occupancy ending on it is now listed; on the real current day it is still in force (consistent with `Occupancy.IsCurrent`). No financial calculation changed (the NPM annual obligation stays ₱10,800).

Space behavior in Phase 1: View everywhere; **Close** reported only where `CloseAccountAsync` allows it (Head, not already closed); Resume/Renew/Reassign/Remove are reported unavailable with `PendingApproval` (monthly space) or `NotApplicableToEventLot` (Fiesta/Araw). Nothing unimplemented is offered. A Fish/Meat vendor-fee account is not a lifecycle source (it is anchored to an NPM stall).

## 1. What exists today (verified)

### Two lifecycles, two financial models

| | Permanent stalls (NPM / NCC / TCC) | Monthly Space Rental (Kanmanggay, Vegetable/Fruit) | Event lot (Fiesta/Araw) |
|---|---|---|---|
| Physical identity | `Stall` row (`FacilityId`, `Section`, `StallNo`, unique index when `Section` is set) | **None.** `ObligationAccount.SubjectLabel` is a free-text label | none; one event period |
| Occupancy / term | `Contract` rows per stall; `StallOccupancy` is derived (start, end, billable end) | The account *is* the occupancy: one continuous `ActiveFrom`..`ActiveTo` interval | one `EventDate` |
| Close | `ToggleStallStatus(close)` → `Stall.Close(date)` | `ObligationWorkflow.CloseAccountAsync` → `ObligationAccount.Close(date)` (SuperAdmin; refuses a date before the last assessed period) | same as monthly |
| Resume | `ToggleStallStatus(reopen)` → `Stall.Reopen` + `ExcuseClosurePeriodAsync` (closed interval written as excused, never back-billed; blocked if section closed or month evidence activated) | **None.** `ActiveTo` cannot be cleared (would erase the gap). UI says "Reopen unavailable: requires an approved successor-account workflow" | n/a |
| Renew | `RenewStallContractCommand` appends a new `Contract`, clamps the outgoing one | **None** (descriptive years/term only) | n/a |
| Reassign | `AssignPastOccupantStall` (new stall for a past occupant), `GetStallReassignmentPreview` (read) | **None**; `CorrectHolderEvidence` refuses a different occupant once periods exist ("close it and open a successor") | n/a |
| Remove | `SoftDeleteStall` (SuperAdmin; closed or expired only; soft delete, "frees the number") | **None** | **None** |
| Duplicate-space guard | DB unique index (section-scoped) + soft-delete | **App-level only:** `SpaceAccountNumbering` under a tenant advisory lock; **no DB constraint** | same |

### Findings that drive the design

1. **Number reuse is currently impossible for space accounts.** `LoadSpaceNumberingAsync` loads every account (open *and closed*) and `Add` refuses any normalized duplicate. Requirement 3 (historical reuse, no overlapping active occupancy) therefore needs an identity/interval model; relaxing the string check alone would remove the only guard.
2. **No stable physical identity for space rentals.** Equal labels are the only link between a predecessor and a successor, which IA-009/IA-068 forbid inferring.
3. **The stall model already has the right primitives** (physical `Stall` + `Contract` terms + derived `StallOccupancy`, closure excusal, `Overlaps`, `AnsweringForMonth`). They are the reference, not code to copy into obligation accounts (the parity handoff is explicit about this).
4. **IA-006 conflict:** Closed Accounts is declared a read-only report, but `ClosedAccounts.razor` already mutates for stalls (Resume, Renew, Renew eligible, Assign new stall, Remove). Space rows there are read-only with the note "open the account workspace". Phase 4 must reconcile this, not extend it.
5. **Lifecycle verbs are scattered:** `ToggleStallStatus` (Close and Resume in one command with a bool), `RenewStallContract`, `AssignPastOccupantStall`, `SoftDeleteStall` on the stall side; `CloseAccountAsync` inside a 527-line workflow on the obligation side. Eligibility rules are duplicated in handlers and in UI (`IsBulkRenewable`, `r.State == …`).
6. **Existing guards to keep:** the role split exactly as audited in section 0 (Stall Close/Resume/Renew/Reassign Admin or SuperAdmin; Stall Remove and every Space account mutation SuperAdmin), tenant check in `Run`, closure refused before the last assessed period, rates are append-only and never retroactive, `ObligationPeriod` is unique per account/month, closure excusal never overwrites a real payment.

## 2. Target architecture

### 2.1 Shared lifecycle application contract (Application layer, no schema)

```text
IAccountLifecycle  (per source: StallLifecycle, SpaceAccountLifecycle)
  Task<LifecycleView>        GetAsync(accountRef, asOf)            // View: state, history, allowed actions with reasons
  Task<LifecyclePreview>     PreviewAsync(accountRef, action, input)// eligibility + consequences + preview version
  Task<Result<LifecycleOutcome>> ExecuteAsync(accountRef, action, input, clientOperationId, expectedVersion)
enum LifecycleAction { Close, Resume, Renew, Reassign, Remove }
record AllowedAction(LifecycleAction Action, bool Allowed, string? ReasonCode, string? Reason, string RequiredRole)
```

- **Source-specific handlers, one contract.** `StallLifecycle` is, in Phase 1, a **read-only adapter** over the existing Stall/Contract data: it reads state and history and states eligibility; it calls and changes none of the existing commands (their tests are the characterization). Routing a mutation through the shared contract is a later, separately designed step. `SpaceAccountLifecycle` is new, owns Kanmanggay and Vegetable/Fruit monthly. Fiesta/Araw is **not** registered: it advertises only View and (existing) Close, never Resume/Renew/Reassign (requirement 7).
- **Eligibility is data, not UI logic:** `GetAsync` returns `AllowedAction[]` with machine reason codes, so Closed Accounts, Operations workspaces and Mobile show the same truth. The UI never re-derives eligibility.
- **Target state for any NEW mutation (not implemented, not available on today's Stall commands):** preview/confirm with a preview version for Resume, Renew and Reassign (stale preview ⇒ conflict). `ClientOperationId` replay returns the original outcome; a changed intent under the same id conflicts. Same pattern as remittance/office-cash turnover.
- **Authority (audited, unchanged):** View = Admin/SuperAdmin. Stall Close/Resume/Renew/Reassign = Admin or SuperAdmin; Stall Remove = SuperAdmin; Space Close = SuperAdmin. A new Space mutation's role is **Pending Approval**. Tenant is resolved from the token, never the request.
- **No new write path for money.** Lifecycle never creates a Collection, SRC, allocation, rate edit or period edit.

### 2.2 Space-rental identity and occupancy model (additive schema, **needs approval**)

```text
SpaceRegistration        -- the physical space, stable root identity
  Id, MunicipalityId, Kind (Kanmanggay | VegetableFruit), NormalizedNumber, DisplayNumber,
  CreatedAtUtc, CreatedBy           -- unique (MunicipalityId, Kind, NormalizedNumber)

ObligationAccount (existing)      -- becomes one OCCUPANCY of a registration
  + SpaceRegistrationId (nullable, FK, tenant-composite)
  + PredecessorAccountId (nullable, FK, immutable once set)
  + OccupancyEndedReason (Closed | Reassigned | Removed)   -- optional, audit-only

SpaceLifecycleEvent (append-only audit/replay)
  Id, MunicipalityId, SpaceRegistrationId, AccountId, Action, EffectiveDate, ClientOperationId (unique per tenant),
  FingerprintHash, ActorId, ActorName, RecordedAtUtc, Reason
```

- **No overlapping active occupancy:** Postgres exclusion constraint on `(MunicipalityId, SpaceRegistrationId, daterange(ActiveFrom, COALESCE(ActiveTo, 'infinity'), '[]'))` (needs `btree_gist`), **plus** the existing advisory lock for pre-validation messages. Replaces today's string uniqueness; historical reuse of a number becomes "a new occupancy of the same registration after the previous one ended".
- **Resume** = new occupancy of the *same registration and same holder evidence* (successor account), starting on a date the office approves; predecessor stays closed, its rates/periods/allocations/Collections/SRCs untouched. The closed gap is never assessed. Proposed date rule (needs approval): first day of a calendar month strictly after the closure month, because a monthly period is indivisible.
- **Renew** (same occupant, continuity) = append a term record to the account (`SpaceContractTerm`: prior-term link, commencement, years, document reference, approval actor/date/evidence). Does not touch rates (rate changes keep using `SetRateAsync`) and does not create a successor account. Whether renewal may span a closed gap is a MEEDO decision (Q3).
- **Reassign** = close the current occupancy (if open) and open a **separate** occupancy for a **different explicit holder** on the same registration. Holder identity is explicit; never matched by name (IA-009/IA-068). The previous holder's balance stays with the previous occupancy and is never transferred.
- **Remove (non-destructive):** a role-restricted, audited *hide/retire* of an inactive occupancy or registration (flag + `SpaceLifecycleEvent`), never a row delete; blocked when assessed periods carry an unpaid balance unless the Head records an explicit reason. No hard-delete capability is introduced; the stall soft-delete path is unchanged.
- **Backfill (migration step, guarded):** create one `SpaceRegistration` per existing normalized `(Kind, label)`; link existing accounts to it. Duplicate/ambiguous normalized numbers are listed for human reconciliation and the migration **stops** rather than guesses a chain (as the parity handoff requires). Existing rows keep their dates.

### 2.3 UI (V3, one consistent surface)

- A shared `AccountLifecyclePanel` (View + allowed actions with reasons, history timeline of occupancies/terms/closures) and one `LifecycleActionDrawer` (preview → confirm), used by Space workspaces and, via adapter, Stall pages.
- Closed Accounts becomes a pure reporting register (IA-006) whose "Action" column is a **link** to the owning workspace's lifecycle panel (`/operations/...?account=...&action=resume`), authorized by the same `AllowedAction` data. Existing Stall actions are moved there in Phase 4 with their current behavior and tests intact; until then they stay and are flagged as an IA-006 exception.

## 3. Phased implementation plan

| Phase | Scope | Schema change | Exit criteria |
|---|---|---|---|
| **0. Decisions** | Close the MEEDO questions in §7; Core Brain approves schema | none | Written rulings in `DECISION_REGISTRY` |
| **1. Contract + stall adapter — COMPLETE (read-only, 2026-10-20)** | `IAccountLifecycle`, `LifecycleAction`, `AllowedAction`; read-only `StallLifecycle` / `SpaceAccountLifecycle` adapters; stall eligibility rules (`IsBulkRenewable`, removable, resumable) into one server-side function; DTO + GET-only endpoint `GET api/AccountLifecycle/{source}/{accountId}` (the typed client is not built yet; it arrives with the Phase 4 UI) | none | Done: no Stall command changed; permission characterization and eligibility tests pass |
| **2. Space identity** | `SpaceRegistration`, FK + predecessor columns, exclusion constraint, guarded backfill; numbering reads registrations; duplicate-space and overlap tests | **additive migration**, authorized separately | Existing 30 obligation tests green; new overlap/duplicate/backfill tests; number reuse after close works only via a new occupancy |
| **3. Space lifecycle commands** | Resume (successor), Renew (term append), Reassign, Remove(retire); previews, replay, concurrency; lifecycle event log | additive (terms + events) | Financial regression suite green; no Collection/SRC/rate/period writes |
| **4. UI** | `AccountLifecyclePanel`, `LifecycleActionDrawer` in Kanmanggay/Vegetable workspaces; Closed Accounts → link-only actions (stall actions relocated); Mobile reads `AllowedAction` where relevant | none | Component tests + visual acceptance by Clint |
| **5. Hardening** | Tenant isolation, concurrency, audit review, docs reconciliation (IA entries, ACTIVE_WORKSTREAMS) | none | Full suites green; handoff written |

## 4. Exact files (expected touch list)

- **New (Application):** `Common/Revenue/Lifecycle/*` (`IAccountLifecycle`, `LifecycleAction`, `AllowedAction`, `LifecyclePreview`, `LifecycleOutcome`), `StallLifecycle.cs`, `SpaceAccountLifecycle.cs`.
- **New (Domain):** `SpaceRegistration`, `SpaceContractTerm`, `SpaceLifecycleEvent`; changes to `ObligationAccount` (registration/predecessor link; no `ActiveTo` clearing).
- **Changed (Application):** `ObligationWorkflow` (delegate Close; numbering from registrations), `SpaceAccountNumbering` (becomes registration-based), request/DTO records.
- **Infrastructure:** `ObligationConfiguration` (+ new configs), one additive migration (+ designer), `AppDbContext` (keep `BeginSpaceAccountTransactionAsync`).
- **Api/HttpClients:** lifecycle endpoints under `api/obligations/accounts/{id}/…` (view, preview, execute) and the stall adapter route; typed client.
- **Client (UI, after contracts exist):** `ObligationWorkspace.razor` (lifecycle panel), `ClosedAccounts.razor` (links), new `AccountLifecyclePanel` / `LifecycleActionDrawer`.
- **Tests:** extend `ObligationAccountTests` (PostgreSQL), new `SpaceLifecycleTests`, `SpaceRegistrationBackfillTests`, stall characterization tests in `EEMOCantilanSDS.Testing/Application/Stalls`, component tests for the panel and Closed Accounts links.

## 5. Compatibility risks

- **Closed Accounts behavior** (IA-006): moving stall actions changes where staff click; keep behavior, change only the entry point, behind visual acceptance.
- **Backfill ambiguity:** normalized duplicates already in a tenant's data; the migration must report and stop, not merge.
- **Exclusion constraint** needs the `btree_gist` extension and cannot be added while overlapping rows exist; pre-check script first.
- **Mobile:** Collector Mobile resolves SpaceAccount identities; successor accounts must keep identity stable for the *registration* while the *account id* changes. Needs the Mobile contract reviewed (Mobile is outside the UI role).
- **Reports/Arrears:** closed predecessors with balances stay Arrears (IA-069); a successor must start with a zero balance. Follow-up/Arrears readers that group by label must group by registration + occupancy, otherwise a reused number could merge two holders' debts.
- **Stall resume vs space resume are intentionally different:** stalls excuse the closed span on the same record; space rentals create a successor occupancy and never assess the gap. A shared UI must label them by outcome, not pretend they are one mechanism.

## 6. Test coverage required (before each phase merges)

- **Tenant isolation:** foreign-tenant account/registration invisible and unmodifiable for view, preview, execute.
- **Concurrency:** parallel resume/reassign/create/import on one registration → exactly one winner; replay of the same operation returns the same successor; changed intent conflicts.
- **Duplicate-space / overlap:** same number reusable only after the previous occupancy ended; overlapping intervals rejected by the database, not just the workflow.
- **Financial regression:** predecessor rates, periods, allocations, Collections and SRCs byte-identical after Resume/Renew/Reassign/Remove; closed gap never assessed; successor starts at zero; no balance transferred; payment history of each occupancy independent.
- **Authorization (audited roles):** Admin may Close/Resume/Renew/Reassign a stall and may not Remove it; the Head alone Removes a stall and closes a space account; a Collector sees nothing; Fiesta/Araw rejects Resume/Renew/Reassign.
- **Stall characterization:** Close/Resume/Renew/Remove outcomes identical through the adapter.

## 7. Business questions MEEDO / Core Brain must answer before Phase 2 — ALL PENDING APPROVAL

1. **Resume date:** is "first day of a calendar month strictly after the closure month" approved? What happens for mid-month resumption?
2. **Resume vs successor:** is resuming the same holder a *new occupancy of the same registration* (proposed) or must it extend the old record?
3. **Renew:** may a renewal bridge a closed gap, and is continuity required (same holder) or can a signed renewal imply a new occupancy?
4. **Holdover / late renewal billing** after the term lapses.
5. **Reassign mid-month:** which holder answers for the handover month (stalls use "latest occupancy wins"; confirm for monthly spaces).
6. **Remove:** is retiring an inactive space account with an unpaid balance ever allowed, and who approves?
7. **Billing-month ownership for Space Rentals:** which holder answers for a month in which occupancy changes or a closed gap ends; stalls use "latest occupancy within the month wins", unconfirmed for spaces.
8. **Scope:** Vegetable/Fruit numbering is server-assigned; do its registrations follow the same registration/occupancy model? (BBQ, Ice Plant and custom sources are out of scope unless approved.)

## 8. Authorization and next step

No schema, migration, database or service change is made by this document. To proceed, Core Brain should (a) answer §7, (b) note that Phase 1 (code only, no schema) is complete and verified, and (c) separately authorize the additive migration for Phase 2 against a disposable database, never the retained municipal database.

## 9. Phase 2 implementation-readiness assessment (planning only; nothing below is implemented)

Phase 1 status: the read-only foundation is in source and tested; the Stall commands and Space workflows are unchanged. Phase 2 is **not ready to start** until the items marked **Blocked** are resolved.

| Area | Readiness | What is needed first |
|---|---|---|
| 1. Stable `SpaceRegistration` identity | Designable now; **Blocked** on a reviewed additive migration and a data preflight | Preflight script (read-only) listing, per tenant, normalized `(Kind, label)` groups with more than one account, accounts with no label, and overlapping intervals; the migration must stop on any ambiguity, never guess a chain. Backfill only after the preflight is clean and approved. Tenant-composite FKs, unique `(MunicipalityId, Kind, NormalizedNumber)`. |
| 2. Safe number reuse and overlap protection | **Blocked** on 1 | Postgres exclusion constraint over `(MunicipalityId, SpaceRegistrationId, daterange)` needs `btree_gist` and a clean preflight. Keep the tenant advisory lock for friendly errors; the constraint is the guarantee. |
| 3. Resume with a successor occupancy | **Blocked** on 1 and on decisions Q1/Q2/Q5/Q7 | Date rule (proposed first day of a month after the closure month), holder carry-over, rate resolved server-side, never assessing the closed gap. Needs a durable operation record (`ClientOperationId` unique per tenant, request fingerprint) and a preview version, because none of the existing Stall commands provide them. |
| 4. Renew / Reassign and billing-month ownership | **Blocked** on Q3/Q4/Q5/Q7 | Term table (prior-term link, commencement, years, document reference, approval evidence) and an explicit handover-month rule. Stall semantics (latest occupancy within the month) are evidence, not authorization. |
| 5. Audited non-destructive Remove | **Blocked** on Q6 | Retire flag + append-only lifecycle event; decision needed for accounts with unpaid balances and for the approving role. No delete. |
| 6. Stall mutations through the shared contract | Optional, after 1–5 | Only with replay/concurrency/version design; until then the existing endpoints remain the only Stall mutation path. |

**Required migrations (additive, separately authorized, disposable database first):** `SpaceRegistrations`; nullable `SpaceRegistrationId` and `PredecessorAccountId` on `ObligationAccounts`; `SpaceContractTerms`; `SpaceLifecycleEvents` (append-only, unique operation id per tenant); `btree_gist` + exclusion constraint. No change to `Collections`, allocations, `ObligationPeriods` or `ObligationRates`.

**Required preflight checks (read-only):** duplicate normalized labels per tenant/kind; accounts whose intervals overlap on the same normalized label; accounts with periods after `ActiveTo`; Vegetable/Fruit server-assigned numbers that are not plain integers; tenants with no default-policy confirmation for lease years.

**Required financial regression tests:** predecessor rates, periods, allocations, Collections and SRCs byte-identical after every lifecycle action; closed gap never assessed; successor starts at zero and no balance transfers; Arrears keeps the predecessor's debt under the original occupant (IA-069); payment history per occupancy independent; replay returns the same outcome; concurrent resume/reassign/create/import on one registration has exactly one winner; foreign-tenant refusal for view, preview and execute.

**Remaining MEEDO decisions (all Pending Approval):** Q1 resume date rule; Q2 resume as successor vs extension; Q3 renewal across a closed gap and continuity; Q4 holdover/late-renewal billing; Q5 handover-month ownership; Q6 removal with outstanding debt and who approves; Q7 billing-month ownership for Space Rentals; Q8 scope beyond Kanmanggay and Vegetable/Fruit.

## 10. Phase 2 + 3 status

Implemented in source and tested on disposable PostgreSQL; see `SHARED_LIFECYCLE_PHASE2_3_HANDOFF_20261020.md`. Q1-Q8 above remain Pending Approval; Resume/Renew/Reassign stay disabled by default.
