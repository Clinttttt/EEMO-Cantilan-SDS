# Mobile NPM request cancellation and source-native charges
October 15, 2026

## Investigation

The existing local NPM request database had an audited `Submitted` request for Josephine Reyes. Existing table constraint `CK_NpmRegistrationRequest_State` permits statuses 1–4 only. Thus treating a collector withdrawal as a Head rejection or removing the row is not valid. A distinct terminal status 5 (Cancelled) is necessary, with its own additive constraint migration.

The unified Mobile charge picker already receives authorized monthly rent periods and utility charges from the server. Monthly rent is collectible here only for a **prepared canonical PaymentRecord**, active collection policy, matching occupancy/collector facility, and unspent available balance. The older `/collect/{Facility}` operation still handles the prior specialized monthly flow, including legacy-authority periods. Never invent a rate or bypass this authority using a direct collection.

## Changes staged in isolated worktree

- `NpmRegistrationRequestStatus.Cancelled = 5`, auditable `NpmRegistrationRequest.Cancel(...)` transition requiring Submitted state, matching version and nonempty idempotency key. Associated amount, SRC, collections, source occupancy, and remittance are never created or modified by this transition.
- Collector-only `POST /api/office-sources/npm-requests/{id}/cancel`; server checks same tenant and original collector, supports exact-operation replay, and blocks post-review withdrawal.
- Mobile history has a quiet **Cancel request** action available only for pending requests, with explicit **Confirm cancellation** / **Keep request** steps, error reporting and refresh. Cancelled requests remain in review history with their own status. Display official market area names instead of enum names.
- Add migration `20261015234000_NpmRegistrationRequestCancellation20261015`: relax just the existing NPM request state check to include status 5, preserving all other constraints and indexes; Down explicitly refuses to erase cancelled audit history.
- New Collection now lists every **server-eligible charge** directly under Items and opens the established charge picker and quote/review. For selected TCC, NCC, BBQ and ICE stalls with no canonical period it shows a rent-specific explanation and a link to the existing facility monthly operation, rather than offering a non-authorized direct payment.

## Testing and safeguards

- Mobile component suite: **143 passed, 0 failed**, including new cancellation and monthly-stall safety tests.
- API Debug build: **0 errors**.
- No new local PostgreSQL migration was applied under this task; the prior NPM request migration remains in effect.
- No real request was cancelled. Josephine Reyes remains pending.
- No new Collection, OR/CT, SRC, cash, revenue, or remittance was created.
- No production/public APK publication or Head-side deployment. The running Windows Debug preview and local API on port 5117 are kept on the prior verified build while the new cancellation migration awaits separate approval.
- Primary checkout retains its protected eight dirty files; all implementation belongs to isolated `fix/mobile-native-records-npm-polish-20261015` worktree.
- **Next acceptance gate:** owner approves the additive status-constraint migration; take/verify a fresh database backup, apply only that migration on local PostgreSQL, then safely restart local API and rebuild/restart Windows Mobile preview. Verify pending→cancelled and that a new request becomes possible afterward, without financial effects. Confirm server-authorized monthly charges against real TCC/NCC records and otherwise retain specialized operation.
