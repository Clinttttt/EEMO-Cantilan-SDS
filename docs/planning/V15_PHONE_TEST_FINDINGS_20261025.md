# CORE BRAIN V15 — Phone testing fixes (2026-10-25)

## Isolation
Working tree: C:\dev\stalltrack\v15-phone-findings-20261025
Branch: fix/v15-phone-findings-20261025 (created from production master 1f57c114).
Claude 1.1's worktrees, tasks, and production have not been touched by this branch.

## Implemented
- Mobile Spaces/Fiesta source list: remove nested list scroll trap on space operations; retain native page scrolling and footer clearance.
- Mobile ECF: source-area filter chips for Fish Area, Meat Area, NPM custom sections, TCC and other assigned facility occupancy sources; vendor registrations excluded. Backend registered-source DTO gains optional SectionLabel read-only field, populated from facility section labels or custom NPM section.
- Mobile WCF: source filter chips from actual eligible utility sources, including Head-defined NPM custom section names. Existing SourceLine already displays section on rows and selected source.
- Web Tabo: preserve legacy attendance, separately read canonical SRC-backed Tabo from tenant-scoped Collection Activity. Combine amounts/counts without creating or adjusting attendance records; show SRC row. Do not misstate totals when canonical feed unavailable or truncated.
- Added Bunit Tabo SRC view and ECF filter coverage; isolated PostgreSQL browse tests include custom NPM metadata.

## Verified
- Full Mobile components: 181 passed, zero failed/skipped.
- Full Web components: 1,463 passed, zero failed/skipped on final rerun.
- Targeted Tabo Bunit regression: 1 passed.
- Targeted ECF group Bunit regression: 1 passed.
- Two PostgreSQL browse tests compiled but SKIPPED: local Docker Desktop Linux daemon unavailable. GitHub CI must run them before merge.
- git diff --check clean at the last verification.
- Device-specific scroll behavior not yet verified on physical Android.

## Deliberately not changed
- Fiesta/Araw pre-event lot collections remain disabled by authoritative date eligibility rules; changing to advance payments requires business-rule approval, ledger review, and tests.
- Mobile canonical Tabo optional OR field NOT added: FeeSchedulePostRequest and fee schedule sync contract have no accountable OR custody/evidence mechanism. Adding only a field would silently discard the OR and misrepresent receipt issuance. Needs separate end-to-end document validation and persistence.
- No production deployment, database migration, Android APK or financial record modifications were made by this worktree.

## Next
- CI (especially PostgreSQL testcontainers) and focused diff review, then merge only when green.
- Build separate Android preview after merged API/Web release and physical device smoke tests.
