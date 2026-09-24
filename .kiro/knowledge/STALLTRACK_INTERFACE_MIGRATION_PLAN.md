# StallTrack Interface Migration Plan

**Status:** Incremental implementation roadmap
**Baseline:** `22f45239adf998d22c33e402aa4c87e2465ac106`
**Target architecture:** [STALLTRACK_INTERFACE_ARCHITECTURE.md](STALLTRACK_INTERFACE_ARCHITECTURE.md)
**Decision registry:** [STALLTRACK_INTERFACE_DECISIONS.md](STALLTRACK_INTERFACE_DECISIONS.md)

## 1. Purpose

This plan moves the current Web Office and Collector Mobile interfaces toward StallTrack Interface System V2 without a big-bang rewrite. Each phase is additive, reviewable, and reversible at the navigation/composition boundary.

The plan changes interface structure only unless a separately authorized business or financial change is created under its own scope, evidence, tests, and decision record.

## 2. Migration principles

1. **Presentation freeze first.** The finalized presentation build remains unchanged. Runtime migration begins only after the presentation.
2. **Add before removing.** Introduce target routes, navigation, and shared structure before retiring current routes or entry points.
3. **Preserve page behavior.** Early phases rehome existing pages; they do not rewrite their commands, queries, calculations, or data sources.
4. **Protect source authority.** Specialized source domains remain authoritative throughout their approved transition periods.
5. **Keep authorization stable.** Navigation visibility may improve, but endpoint, page, guard, and role authorization remain unchanged unless separately approved.
6. **Reconcile financial views like for like.** Any report composition change compares the same tenant, facility, occupancy scope, period, as-of date, and money/date basis.
7. **Keep old routes working.** Bookmarks, training material, activation/token links, payment callbacks, and contextual links remain compatible.
8. **Expose capability only when usable.** Accountable Forms, Remittance, classified reports, target attainment, WCF dual entry, and transportation classes remain hidden until their domain capabilities are approved and shipped.
9. **Separate structural and financial work.** No interface phase may hide a financial refactor inside navigation or component extraction.
10. **Ship small role-complete slices.** A phase is complete only when affected Head/Admin/Collector/Payor paths are reachable, authorized, and regression-tested.

## 3. Baseline and compatibility posture

At the baseline:

- Web has 77 `@page` route directives across 65 routed components, including facility aliases.
- Collector Mobile has 12 routed components.
- Web global navigation exposes generic pages, every configured facility, Reports, Audit, Export, and Settings.
- Mobile bottom navigation is Menu, Records, Reports, Profile.
- current financial and operational writers remain source-specific;
- current route and role behavior is production behavior and must survive the structural migration.

### Current-to-target compatibility strategy

Migration uses three steps for a route family:

1. **Alias:** add the target route to the current component or a thin route host without removing the current route.
2. **Canonical link:** update navigation and newly generated links to the target route while old deep links continue to resolve.
3. **Retirement review:** consider redirecting or removing a legacy route only after the retirement conditions in Section 8 are met.

Aliases must preserve:

- path parameters;
- query parameters and selected filters;
- authorization and role behavior;
- tenant context;
- browser history behavior where relevant;
- payment/authentication callback contracts;
- direct facility/report context.

### Route family map

| Current route or family | Target canonical route | Initial compatibility action |
|---|---|---|
| `/menu` | `/overview` | Add alias to existing dashboard component; keep `/menu` |
| No current Facilities hub; sidebar links directly to facility routes | `/operations` | Add a landing backed by the current facility catalog; preserve every direct facility route |
| `/npm`, `/facility/npm` | `/facilities/npm` | Add canonical facility alias; preserve both routes |
| `/tcc`, `/facility/tcc` | `/facilities/tcc` | Same |
| `/ncc`, `/facility/ncc` | `/facilities/ncc` | Same |
| `/bbq`, `/facility/bbq` | `/facilities/bbq` | Same |
| `/ice`, `/facility/ice` | `/facilities/ice` | Same |
| `/slh`, `/facility/slh` | `/facilities/slh` | Same |
| `/trm`, `/facility/trm` | `/facilities/trm` | Same |
| `/tpm`, `/facility/tpm` | `/facilities/tpm` | Same |
| `/facility/{Slug}` | `/facilities/{facility}` | Add plural canonical alias; preserve dynamic route |
| `/import/{Facility}` | `/facilities/{facility}/accounts/import` | Add contextual host/alias; preserve facility parameter |
| `/import-history/{Facility}` | `/facilities/{facility}/activity/import` | Add contextual host/alias |
| `/profile/{FacilityId}/{StallKey}` | Future `/accounts/{accountId}` or `/occupancies/{id}` | Keep current route until stable canonical identity exists |
| `/facility/slh/transaction/{OwnerName}` | Future `/facilities/slh/activity/{id}` | Keep current route until a stable record ID is available |
| `/vendors` | `/accounts` | Do not claim `/accounts` until administrator accounts move |
| Current `/accounts` | `/admin/access/administrators` | Add Administration alias first; retain old route |
| `/collectors` | `/admin/access/collectors` | Add alias; preserve contextual report/app links |
| `/transactions` | `/collections/activity` | Add alias; use Collection Activity in new navigation |
| `/online-payments` | `/collections/online` | Add operations alias; later separate provider configuration composition |
| `/reports/collections` | `/collections/status` | Add alias; no command changes |
| `/reports/follow-up` | `/monitoring/follow-up` | Add alias |
| `/reports/follow-up/history` | `/monitoring/follow-up/history` | Add alias |
| `/reports/closed-accounts` | `/accounts/ended-occupancies` | Add only after account namespace is available |
| `/reports` | `/reports/receivables/financial-position` | Keep `/reports` as library/compatibility entry |
| `/reports/financial-summary` | `/reports/management/financial-summary` | Add alias |
| `/reports/month-end` | `/reports/cash/monthly-collections` | Add alias |
| `/collectors/{id}/report` | `/reports/cash/collectors/{id}` | Add parameter-preserving alias |
| `/{facility}/reports` and `/custom1..5/reports` | `/facilities/{facility}/reports` | Add aliases individually; do not remove legacy custom routes |
| `/reports/slaughterhouse` | `/facilities/slh/reports/activity` | Preserve until SLH report entry points share one definition |
| `/stall-holders` | `/reports/operational/stallholders` | Add alias |
| `/list-stall-holders` | `/facilities/{facility}/reports/stallholders` | Preserve current query/context until parameterized target exists |
| `/export` | Scoped export action in `/reports` | Keep compatibility entry until all export paths have owners |
| `/settings` | `/admin` | Add Administration landing alias |
| `/settings/facilities` | `/admin/business/facilities` | Add alias |
| `/settings/revenue` | `/admin/business/revenue-classifications` | Add alias |
| `/office-profile` | `/admin/office/profile` | Add alias |
| `/audit-trail` | `/admin/system/audit` | Add alias |
| `/settings/backups` | `/admin/system/backups` | Add alias |
| `/login`, activation, reset, verification, MFA, Payor auth/payment callbacks | Existing routes | Do not rename as part of interface migration |

## 4. Role-visibility migration

Navigation visibility and authorization are separate controls.

### Target visibility

| Role | Navigation visibility |
|---|---|
| Head | All released Web workspaces and authorized Administration areas |
| Admin | Overview, Operations, Collections, Payors & Accounts, Monitoring, Reports; no unusable Head-only global rows |
| Collector | Collect, Activity, Summary, Me in Mobile only |
| Payor | Home, Balances, Payment & Receipt History, Profile |

### Rules

- Removing a locked navigation row does not relax or replace authorization.
- Existing `[Authorize]`, API policies, `AdminManagementGuard`, MFA rules, and tenant guards remain authoritative.
- A protected action reached from an otherwise accessible record must explain the required authority at the action boundary.
- Administration may be absent for Admin when no released administrative destination is usable.
- Role visibility changes require component/navigation tests and direct-route authorization tests.

## 5. Phase sequence

No phase implicitly authorizes the next. Each phase requires its own implementation task and review.

## Phase A — Navigation vocabulary, visibility, and additive aliases

### Objective

Establish the target Web workspace vocabulary and compatibility routes while preserving all current page bodies.

### Structural change

- Add the released workspace groups: Overview, Operations, Collections, Payors & Accounts, Monitoring, Reports, Administration.
- Keep Accountable Forms absent.
- Add a single Facilities landing entry using existing facility catalog data.
- Add only the bounded canonical aliases required by the first slice in Section 9.
- Update newly generated global navigation links to canonical aliases.
- Hide unusable Head-only rows from Admin navigation.
- Correct mismatched global links, including Recent Transactions linking to Collection Activity rather than Audit Trail.

### Business/financial behavior that must not change

- No page-body workflow change.
- No query, command, DTO, API, rate, obligation, collection, report, delinquency, or occupancy change.
- No permission expansion.
- No removal of an existing route.

### Likely files/surfaces

- `EEMOCantilanSDS.Client/Components/Pages/Shared/Sidebar.razor`
- Web layout/shell route helpers
- existing routed components receiving aliases
- a bounded Facilities landing component
- component/navigation tests

### Dependencies

- Interface architecture and decision registry accepted.
- Final presentation completed.
- Canonical label and first-alias list agreed.

### Risk

**Medium.** Main risks are missing destinations, active-link errors, role visibility regressions, and broken deep links.

### Regression tests

- old and new route resolution;
- query-string preservation;
- Head/Admin navigation visibility;
- direct-route authorization unchanged;
- every configured/custom facility remains reachable;
- standalone auth/token/callback routes retain their shell behavior;
- dashboard and facility data render unchanged.

### Acceptance criteria

- all 77 current Web route directives remain usable;
- target aliases in scope reach the same current components;
- every current capability remains reachable for its authorized role;
- no future workspace appears;
- no financial value or command behavior changes;
- scoped CSS remains brace-balanced if any shell CSS is touched.

### Rollback

Revert navigation links and target aliases as one bounded commit. Existing routes and page bodies remain available throughout, so rollback does not require data work.

## Phase B — Shared page-structure primitives

### Objective

Standardize page composition without changing domain workflows.

### Structural change

- Introduce `WorkspaceHeader`, scope/filter primitives, `TableShell`, summary primitives, and standard loading/empty/error states.
- Migrate a small representative set of non-financial pages before broad adoption.
- Keep component APIs presentation-focused.

### Business/financial behavior that must not change

- No business arithmetic moves into presentation components.
- No command or query is consolidated merely because pages look similar.
- Existing report and workflow data remain byte-for-byte equivalent at their view-model boundary.

### Likely files/surfaces

- Web shared components and scoped CSS
- selected low-risk list/detail pages
- component tests

### Dependencies

- Phase A shell vocabulary.
- Agreed component responsibilities from the target architecture.

### Risk

**Medium.** Broad shared CSS or component use can create cross-page regressions.

### Regression tests

- component render tests;
- action and filter event tests;
- loading/empty/error states;
- responsive table access;
- scoped CSS brace check.

### Acceptance criteria

- migrated pages use the standard anatomy;
- page actions and data are unchanged;
- components contain no duplicated financial calculations;
- unmigrated pages continue to work.

### Rollback

Revert page-by-page adoption while leaving unused primitives in place or remove them if no consumers remain.

## Phase C — Facilities hub and selected-facility shell

### Objective

Replace global facility-list growth with one hub and consistent facility context while preserving every specialized workflow.

### Structural change

- Expand the Facilities landing into the canonical operation selector.
- Add facility switcher and context header.
- Provide shared structural slots for Overview, Work, Accounts/Participants, Activity, and Reports where applicable.
- Update facility report and import links to retain selected facility context.
- Remove permanent facility rows from the global sidebar only after the hub is proven.

### Business/financial behavior that must not change

- NPM daily and month-settlement logic remains specialized.
- Monthly-rental, TPM, TRM, SLH, utilities, and custom-facility writers remain unchanged.
- Effective-dated rates and tenant facility naming remain authoritative.
- Facility KPIs and report figures retain current sources.

### Likely files/surfaces

- `Components/Pages/Menus/Facilities/`
- existing shared facility-page components
- facility catalog/state services
- facility reports and import entry points
- navigation/component tests

### Dependencies

- Phase B structural primitives.
- Tenant-resolved facility catalog and active-state behavior.

### Risk

**Medium-high.** Every office workflow enters through a facility, and specialized pages have different action patterns.

### Regression tests

- all standard and custom facilities appear correctly;
- authorization and tenant scoping;
- facility switcher preserves only valid context;
- primary workflow actions still invoke the same commands;
- facility report/import links retain scope;
- Cantilan figures unchanged.

### Acceptance criteria

- one Facilities global entry is sufficient to reach every configured facility;
- switching facility never maps one facility's filters or record identity onto another;
- specialized Work sections remain intact;
- removing sidebar facility rows does not add more taps to a recent/resume path than agreed.

### Rollback

Restore permanent sidebar facility links while keeping canonical facility aliases. The specialized pages require no rollback.

## Phase D — Payor, account, space, and occupancy hierarchy

### Objective

Create a clear business-account workspace without changing liability attribution.

### Structural change

- Move administrator accounts to Administration routes before claiming `/accounts`.
- Rehome Vendors & Stalls as Account Registry.
- Compose account detail from space, payor, current occupancy/term, obligations, collections, and earlier terms.
- Rehome ended occupancy lifecycle and balances.
- Preserve current profile routes until stable account/occupancy IDs are available.

### Business/financial behavior that must not change

- `StallOccupancy.AnsweringForMonth`, `DomainRules.TermLastDay`, past contract rate, and source obligation calculations remain unchanged.
- Current and earlier-term balances remain separate.
- Renew/reopen/close/remove commands and authorization remain current behavior.
- A new holder never inherits a prior holder's balance through UI composition.

### Likely files/surfaces

- `Menus/Accounts.razor`
- `Menus/Vendor.razor`
- `Shared/Actions/Profile.razor`
- `Reports/ClosedAccounts.razor`
- related route/link helpers and component tests

### Dependencies

- Administrator-account namespace migration.
- Decision on user-facing Account and Vendor terminology.
- Stable identity strategy before canonical account detail routes replace stall-key routes.

### Risk

**High.** Incorrect composition can misattribute historical liability or imply one combined balance.

### Regression tests

- current versus prior occupancy ownership;
- handover month attribution;
- past contract rate;
- active/ended term separation;
- renew/reopen/close actions;
- tenant scoping;
- old profile route compatibility.

### Acceptance criteria

- account detail identifies space, payor, and occupancy/term separately;
- every displayed balance states its term/scope;
- figures match current pages for identical scope;
- current user-account and business-account routes do not collide.

### Rollback

Return navigation to Vendors & Stalls and Closed Accounts while preserving new aliases. Do not migrate or rewrite data during this phase.

## Phase E — Collections and Monitoring separation

### Objective

Move mutable operational work out of Reports and give collection activity and attention queues stable homes.

### Structural change

- Rehome Transactions as Collection Activity.
- Rehome Collection Manager as Collection Status & Corrections.
- Rehome Follow-up Queue, history, exceptions, expired occupancies, and ended balances under Monitoring.
- Split online-payment operations from provider configuration at the composition/navigation boundary.
- Keep contextual actions linked to their current components until later page refinement.

### Business/financial behavior that must not change

- Current specialized collection writers remain authoritative.
- Current OR validation, closure, excusal, follow-up, and online-payment lifecycle behavior remain unchanged.
- Delinquent remains one fully elapsed unpaid month; three months remains a severity boundary only.
- No Arrears qualification is introduced.
- No generic `Collection` writer cutover occurs.

### Likely files/surfaces

- `Menus/Transactions.razor`
- `Reports/CollectionExceptions.razor`
- `Reports/FollowUpQueue.razor`
- `Reports/PastFollowUpQueue.razor`
- `Menus/OnlinePayments.razor`
- route/link helpers and tests

### Dependencies

- Phase A aliases.
- Phase D account context for account-linked actions.
- Correction-authority and online-payment-ownership decisions where the interface exposes choices.

### Risk

**High.** Pages perform real writes despite current report-like names.

### Regression tests

- every existing command path;
- current role authorization;
- NPM closure/excusal effects;
- OR encoding and uniqueness behavior;
- online Awaiting OR lifecycle;
- delinquency and severity bands;
- ended-occupancy separation;
- alias/deep-link coverage.

### Acceptance criteria

- no mutable page remains classified primarily as a Report;
- the same actions remain available to the same roles;
- report figures and source authority are unchanged;
- no future collection/document capability appears.

### Rollback

Restore old navigation categories and links. Since commands, components, and routes remain, operational rollback is structural.

## Phase F — Reports architecture

### Objective

Create a report library with explicit Receivables, Cash Revenue, Management, and Operational families while preserving official figures.

### Structural change

- Add ReportShell and explicit scope/basis headers.
- Classify existing read-only reports.
- Make facility-context and global entries resolve to the same report definition where their semantics match.
- Convert Export Data into report-owned export actions only after every export has a clear owner.
- Separate printable documents from operational lifecycle controls.

### Business/financial behavior that must not change

- Current production report sources remain authoritative until a separately approved cutover.
- `Collection`/`CollectionLine` shadow data does not become an official source.
- Collection Efficiency remains separate from future Revenue Target Attainment.
- Obligation-period, collection-date, and document-date bases remain distinct.
- No historical installment, classification, document, or vehicle detail is invented.

### Likely files/surfaces

- `Components/Pages/Reports/`
- `Menus/Report.razor`
- facility report pages
- print/PDF/CSV helpers
- report query handlers only if a separately reviewed structural adapter is required

### Dependencies

- Operational pages moved out in Phase E.
- EEMO confirmation of the official report/document set.
- Shared report and scope primitives.

### Risk

**High.** Similar labels can conceal different period, lifetime, assessment, or receipt bases.

### Regression tests

- like-for-like reconciliation by tenant, facility, occupancy scope, period, as-of date, and basis;
- Cantilan-unchanged financial tests;
- print/PDF/CSV content and scope;
- central versus facility entry equality;
- no mutation controls in Reports.

### Acceptance criteria

- every report states scope and basis;
- matching definitions produce identical figures from all entry points;
- official reports remain recognizable and complete;
- mutable controls are absent from Reports;
- future classified/target reports remain hidden.

### Rollback

Keep current report routes and page components available. Revert the library and ReportShell adoption for affected pages; no data rollback is required.

## Phase G — Administration and configuration separation

### Objective

Replace the generic Settings bucket with explicit administrative domains.

### Structural change

- Establish Business Configuration, People & Access, Office Setup, and System Administration.
- Split provider configuration from online-payment operations.
- Keep collector app setup contextual to collector administration.
- Move facility history out of configuration.
- Move personal password/MFA to the signed-in user menu.

### Business/financial behavior that must not change

- Existing Head/Admin authorization remains unchanged.
- Effective-dated revenue policy and rate behavior remain unchanged.
- MFA, peer-Head restrictions, tenant scoping, audit, backup, and restore behavior remain unchanged.
- Revenue Setup does not become authoritative for current money merely because it moves.

### Likely files/surfaces

- `Menus/Settings.razor`
- `Menus/Accounts.razor`
- `Menus/Collector.razor`
- `Menus/FacilityConfiguration.razor`
- `Menus/RevenueSetup.razor`
- `Menus/OfficeProfile.razor`
- `Menus/AuditTrail.razor`
- `Menus/Backups.razor`
- provider-configuration portion of `OnlinePayments.razor`

### Dependencies

- Phase A Administration routes.
- Provider-configuration ownership decision.
- Phase D namespace migration.

### Risk

**Medium-high.** Security and configuration boundaries must remain fail-closed.

### Regression tests

- Head/Admin route and navigation visibility;
- direct endpoint authorization;
- `AdminManagementGuard` behavior;
- MFA/security flows;
- facility/rate/revenue effective-date presentation;
- backup/restore permissions;
- provider secret masking and access.

### Acceptance criteria

- every administrative subject has one owner;
- daily operations are absent from Administration;
- Admin sees no unusable Head-only global destinations;
- sensitive actions retain current guards.

### Rollback

Restore Settings as the navigation directory while retaining target aliases. Do not roll back configuration data because this phase does not change it.

## Phase H — Collector Mobile navigation and sync architecture

### Objective

Adopt Collect, Activity, Summary, and Me without changing field collection semantics.

### Structural change

- Rename/recompose Menu as Collect.
- Add Resume Current Work.
- emphasize assigned/available facilities and remove unassigned facilities from routine navigation;
- rename Records as Activity and Reports as Summary;
- add persistent sync/connectivity state and Sync Center;
- keep Collect selected inside specialized facility workflows;
- tenant-resolve facility names throughout.

### Business/financial behavior that must not change

- Billing-archetype routing remains intact.
- Specialized NPM, monthly, TPM, TRM, and SLH capture flows remain source-owned.
- `ClientOperationId`, queue persistence, retry safety, and sync-result handling remain intact.
- Server-issued session business date remains primary; device clock remains fallback.
- No AccountableDocument, Cash Ticket custody, remittance, WCF, or vehicle-class workflow is added.

### Likely files/surfaces

- `EEMOCantilanSDS.Mobile/Components/Shared/BottomNav.razor`
- Mobile Menu, Record, Report, Profile, and facility pages
- navigation/session helpers
- `MobileSyncService` consumers; service behavior should not need redesign
- Mobile component/unit tests

### Dependencies

- Collector validation of labels and resume behavior.
- Safe persisted definition of current/resumable work.
- Signed release process for any collector-visible change.

### Risk

**High.** Field efficiency, offline confidence, and update adoption are operationally critical.

### Regression tests

- session restoration and transient-offline preservation;
- assignment and archetype routing;
- one- and two-tap access to capture;
- queued/failed/rejected/storage-fault states;
- idempotent replay;
- server business date;
- update and sign-out behavior;
- no unassigned facility entry in routine navigation.

### Acceptance criteria

- collector reaches assigned capture work without additional required taps;
- sync state is visible from Collect and Activity;
- queued items remain visible in their workflow and Sync Center;
- every specialized capture produces the same request and result as before;
- release version and signed APK requirements are satisfied when this phase is actually published.

### Rollback

Restore old bottom-nav labels and landing composition. Preserve the offline queue store and never clear pending operations as part of UI rollback.

## Phase I — Visual-system refinement

### Objective

Apply the restrained professional visual system after structural ownership is stable.

### Structural change

- normalize density, typography, spacing, table rhythm, money emphasis, and semantic status presentation;
- remove oversized administrative heroes where WorkspaceHeader is established;
- retain restrained navy/gold identity on white/neutral work surfaces.

### Business/financial behavior that must not change

- No calculation, workflow, route, role, API, or data-source change.

### Likely files/surfaces

- scoped component/page CSS and design tokens only after each affected page is structurally migrated.

### Dependencies

- Relevant structural phases complete.

### Risk

**Medium.** One unbalanced scoped-CSS brace can corrupt the full bundle.

### Regression tests

- visual/component snapshots where maintained;
- responsive tables and keyboard/focus access;
- print layouts;
- scoped CSS brace validation;
- route smoke coverage.

### Acceptance criteria

- administrative pages are compact and readable;
- important money values dominate without decorative color;
- tables preserve professional density and column access;
- semantic state never relies on color alone.

### Rollback

Revert page/component style slices independently. Avoid one cross-application style rewrite.

## 6. Cross-phase release and validation rules

- Do not combine a navigation migration with a financial behavior change.
- Money, reporting, tenancy, or authorization changes discovered during interface work become separate defects/tasks with failing-before-fix tests.
- Run the unit and component suites separately when runtime implementation begins; integration tests remain a separate Docker/Testcontainers run.
- Any Mobile runtime phase requires a signed release APK and coordinated version advertisement before collectors receive it.
- Any push to `master` requires production verification under the established deployment process. This documentation task does not push or deploy.
- Every route phase checks standalone/chrome-less behavior for login, activation, reset, verification, MFA, Payor, error, and not-found routes.

## 7. Rollback strategy

The migration is designed so rollback is primarily link and composition rollback:

- old routes remain available;
- page bodies and writers remain in place during rehoming;
- aliases can be removed or navigation links reverted without data migration;
- shared components are adopted page by page;
- facility and report sources remain unchanged until separate approved cutovers;
- Mobile navigation rollback must preserve the offline operation store and pending items.

If a phase would require destructive data rollback, it has exceeded this interface migration's scope and must be split into a separate architecture change.

## 8. Legacy route and navigation retirement

A legacy route may be considered for retirement only when all conditions are met:

1. the canonical route has shipped successfully;
2. internal navigation and generated links use the canonical route;
3. role and deep-link tests cover both paths;
4. published office materials, QR codes, bookmarks, and external integrations have been reviewed;
5. route parameters and query state have an equivalent target representation;
6. the route is not an authentication, token, activation, callback, download, or other externally held contract;
7. at least one complete office training and release cycle has passed;
8. production logs or equivalent evidence show no required legacy use;
9. removal has an explicit task and rollback plan.

Legacy navigation labels may be retired before route aliases when the target labels have been communicated and every old destination remains reachable.

## 9. Recommended first post-presentation slice

The audit recommendation remains correct with a narrower alias set.

### Included

- target Web navigation vocabulary;
- role-appropriate visibility;
- additive aliases for `/overview`, `/collections/activity`, `/monitoring/follow-up`, and `/admin`, plus `/operations` as the Facilities landing;
- one Facilities landing entry backed by the existing facility catalog;
- current facility routes retained;
- current page bodies retained;
- correction of clearly mismatched global links;
- navigation/route/component tests for this bounded change.

### Explicitly excluded

- report rewrites or ReportShell migration;
- facility workflow or facility-page rewrites;
- account-detail redesign;
- financial or source-domain refactoring;
- API, DTO, database, or migration changes;
- Mobile navigation changes;
- visual-system redesign;
- `Collection`/`CollectionLine` writer cutover;
- AccountableDocument;
- Cash Ticket inventory/custody;
- remittance;
- classified reporting;
- revenue targets;
- WCF or transportation-class workflows.

### First-slice acceptance criteria

- all current routes remain valid;
- the small target alias set reaches existing components;
- Head and Admin see only usable global destinations;
- every facility remains reachable through the hub and legacy route;
- no page-body command, calculation, or data source changes;
- no current figure changes for Cantilan;
- future workspaces remain absent;
- presentation behavior was not modified before the presentation.

## 10. Definition of completion for Interface System V2

Interface System V2 is complete when:

- released Web capabilities use the canonical workspace model;
- one Facilities hub and selected-facility shell reach every configured facility without global-sidebar growth;
- specialized facility workflows remain domain-owned;
- business accounts are distinct from login accounts and current/prior occupancies are explicit;
- mutable work is absent from Reports;
- Reports state scope, date basis, and money basis;
- Administration is separated into Business Configuration, People & Access, Office Setup, and System Administration;
- Mobile uses Collect, Activity, Summary, and Me with visible sync state and Sync Center;
- current external and compatibility routes either remain supported or have completed the retirement process;
- Head/Admin/Collector/Payor role paths are verified;
- no future capability is exposed before its authoritative implementation;
- financial reconciliation and Cantilan baseline tests pass for every affected report surface;
- the decisions registry contains no unacknowledged contradiction between interface language and domain behavior.

Completion of Interface System V2 does not require Accountable Forms, remittance, classified cash reporting, target attainment, WCF dual entry, or transportation classes to be shipped. It requires their eventual placement to remain coherent and their navigation to remain hidden until functional.
