# V15 collector phone feedback — October 25, 2026

Branch: `fix/v15-phone-feedback-20261025`, isolated worktree `C:\dev\stalltrack\v15-phone-feedback-20261025`, based on the verified V15 production master.

## Changes

- Fiesta/Araw event-lot discovery now includes previous event dates even when the account was explicitly closed. Source selection requires an existing approved unpaid event-period balance; future event dates remain disabled. Mobile post continues to use the original event date for settlement identity, current collector business date for cash, caller operation authorization, tenant scoping and idempotency. Monthly spaces remain unchanged.
- ECF and WCF section filters remain data-driven (Fish, Meat, Vegetable, Head-defined custom NPM sections, other assigned sources) but use one horizontal, swipeable tab strip matching New Collection; they no longer wrap to a second line.
- Records canonical card shows the SRC without appending an OR/CT suffix. The official document evidence remains accessible in the collection detail sheets.
- Multiple-collection detail sheet uses a fixed summary and Close button, a scrolling middle list, amounts aligned beside distinct collection headings, and unchanged SRC, instrument, document and item evidence.

## Local verification

- Mobile component tests: 181 passed / zero failed / zero skipped.
- Targeted Docker/PostgreSQL Fiesta/Araw theory: 2 passed / zero failed / zero skipped, including future-date rejection, late closed-event selection, partial posting, idempotent replay, final settlement, and fully-paid disabling.
- These checks do not replace CI or physical Android visual review. Responsive overflow and sticky modal footer require device acceptance before calling the UI verified.

## Safety and release

No schema migration, production data change, rollback, deployment, merge or APK publication in the worktree. Do not change other agents' worktrees. CI and protected release workflow are mandatory before any approved promotion. Earlier V15 signed APK remains the currently installed version until a later approved build.
