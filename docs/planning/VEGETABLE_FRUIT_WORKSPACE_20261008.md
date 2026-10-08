# Vegetable / Fruits workspace — 2026-10-08

Status: implemented and automated validation passed; user visual acceptance pending. Not production deployed or integrated into the local integration branch yet.

Physical checkout: `C:\dev\stalltrack\eemo`. Clean starting integration HEAD: `4b4be8f8`. Feature branch: `codex/vegetable-fruit-space-workspace`. Account/source authority commit: `f21431aa`. Workspace/Mobile presentation commit: `cfef4261`. No additional checkout was created.

## Current ruling and source boundary

The October 8 workspace instruction preserves one official `VEGETABLE_FRUIT_SPACE_RENTAL` classification and separates two collection modes. Temporary/open-space rental remains independent of permanent NPM Vegetable stalls and Kanmanggay.

- Monthly uses `ObligationKind.VegetableFruitSpaceRental` (numeric value 4), source-owned actual occupant, approved effective monthly rental, assessed periods and canonical allocations. The existing `VegetableWholePayment` policy resolves OR. Contract reference is optional and applies only to signed-contract occupancy. Closing preserves historical periods and money.
- Daily retains the existing governed `DailyTransaction` CT writer, amount rule, assignment and channel gates. It creates no monthly account or assessment and needs no master Business Payor.
- New anonymous governed `WholePayment` posts are rejected with `MONTHLY_ACCOUNT_REQUIRED`. Successful historical posting retries resolve before this gate and return the original Collection/SRC. Historical Whole Payment receipts remain readable; they are not rewritten or automatically matched to a new account by name. The Head must choose the correct effective month when opening an account; this change does not infer historical liability or credit from payer text.
- Mobile New Collection discovers registered Monthly spaces as typed Space Account sources and offers OR period balances; source-less Daily sessions retain CT. The focused Daily screen points Monthly users to New Collection. Existing queued, unposted anonymous Whole Payment intents need review against a registered account; they are not converted silently.
- Official Monthly Income uses the unchanged canonical classification reader, combining valid historical Whole Payment, Monthly account settlements and Daily CT money once.

## Workspace and numbering

`/operations/vegetable-fruit` defaults to Monthly and preserves each view's state when switching modes. Monthly reuses `ObligationWorkspace`; Daily shows name/SRC/collector/net amount, search and month scope, with no monthly remaining balance. Monthly reports show account position and the complete official revenue row; Daily/historical activity remains available through the separate report link. Head-only Daily setup reuses `GovernedServiceWorkspace` and its existing configuration API.

Both Vegetable/Fruits and Kanmanggay manual Add New drawers omit Space No. The server allocates and returns the number, which remains in the register. Vegetable/Fruits import omits numbering and sends blank numbers; its API rejects supplied numbers. Existing Kanmanggay/event import may preserve supplied numbers because those accepted import workflows already support explicit historical register identities; their numbering behavior is unchanged. Ordinary blank-number imports continue to use server allocation under the tenant lock.

Shared presentation parameters cover server numbering, operation/subject name, effective-month label, reports/import links and mode-switch placement. Contract eligibility and monthly period semantics remain domain-owned.

## Persistence

The enum is numeric, but three existing PostgreSQL check constraints restrict allowed kinds/account shapes/source holders. Migration `20261008002733_VegetableFruitMonthlySpaceAccounts` widens only these constraints to admit kind 4. No table, property, historical row or Collection is removed or rewritten. It was exercised in isolated PostgreSQL tests and applied by the normal API startup workflow to the verified localhost development database. Migration history and all three actual constraints were checked afterwards. Production migration/deployment is outside this session.

## Validation

- Focused governed-service Unit tests: **24 passed / 0 failed / 0 skipped**. Focused workspace/setup/report/accessibility Web checks: **65 passed / 0 failed / 0 skipped**.
- Full Unit: **2,525 passed / 0 failed / 0 skipped**.
- Full PostgreSQL Integration: **369 passed / 0 failed / 7 skipped** (376 total). The seven skips require optional historical snapshot data. An earlier Docker-unavailable run skipped database tests; it was superseded after starting Docker. The first real full run exposed an obsolete expectation of two anonymous Vegetable/Fruits choices; it now asserts the single Daily choice. The complete rerun passed.
- Full Web components: **750 passed / 0 failed / 0 skipped**. Full Mobile components: **70 passed / 0 failed / 0 skipped**.
- API Release: **0 warnings / 0 errors**. Client Release: **70 warnings / 0 errors**. Mobile Windows Release: **16 warnings / 0 errors**. Existing package/compiler warnings remain; no APK was built or published.
- Financial regression proof: temporarily mapping the new kind to Kanmanggay and restoring anonymous Whole posting produced **2 failed / 1 passed / 0 skipped**; both defects were restored before the passing suites. PostgreSQL tests also prove registered Monthly OR plus Daily CT and valid historical Whole receipts contribute once, partial allocation leaves the authoritative remaining balance, closure retains history, import numbering is tenant-scoped, and a historical successful Whole retry returns the original SRC.
- `git diff --check` passed. All Web scoped CSS source braces passed; the changed stylesheet and generated Release Client bundle were also verified balanced.

## Local runtime and remaining gate

API Release (`https://localhost:7097`), Client Release (`https://localhost:7167`) and freshly rebuilt Windows Mobile Debug were launched from the primary checkout at `cfef4261`. Process paths were verified. Mobile DLL UTC timestamp **2026-10-08 11:50:54** is newer than the application commit (**11:49:01 UTC**). API health and authenticated space-operations, Vegetable/Fruits Monthly workspace and Daily activity reads returned **200**. No test Collection was posted to the development database by this runtime verification.

Browser control failed to initialize with `helper_unknown_error: setup refresh had errors`. The user was asked to verify `/operations/vegetable-fruit` and `/operations/kanmanggay`: Monthly/Daily state retention, register/actions/import/reports, Daily without Remaining, no manual Space No. input, Vegetable/Fruits import without numbering, and the assigned number after saving a local account. The reply was **“I’ll check next”**. That gate remains pending; prior acceptance of Terminal/Fish/Meat/report screens does not accept this new workspace.

After the user confirms this checkpoint, finish the documentation acceptance record, fast-forward the feature branch into `integration/report-governance-ui` in this same checkout, then restart affected components from that integrated branch. No push, master merge, production migration/data change/deployment or APK publication is authorized.
