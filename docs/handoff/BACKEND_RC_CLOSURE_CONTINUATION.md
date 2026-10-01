# StallTrack V3 — Backend RC Closure Cloud Handoff

## Purpose
Continue the backend release-candidate closure task after a Claude Code Cloud session was teleported locally and hit the local weekly usage limit.

## Branch / state
- Branch: `backend/v3-rc-closure`
- Base: `b4e50415` (accepted V3 UI/integration state)
- Current HEAD: `03809525`
- Current commit: `wip(utilities): enforce direct-only new utility assessment basis`
- Working tree was clean immediately after the WIP commit.
- The branch is pushed to `origin/backend/v3-rc-closure`.

## What was completed before the local limit
The utility direct-amount closure was started and committed as a bounded WIP.
Six files were changed:
- RecordUtilityReadingCommand.cs
- RecordUtilityReadingCommandHandler.cs
- UtilityBillEntryDto.cs
- GetUtilityBillForEntryQueryHandler.cs
- UtilityBill.cs
- GetUtilityBillForEntryQueryHandlerTests.cs

The code now starts enforcing that NEW current utility assessments use DirectApproved only, while recorded historical Metered parts can preserve their recorded evidence.
## Exact stopping point
The local Claude had just moved from the entry-query tests to:
"Now handler tests. Checking how existing tests build an NPM stall."

The session then hit the local weekly usage limit.

Do NOT treat the utility slice as finished just because it has a WIP commit.
The next agent must:
1. inspect commit 03809525;
2. finish command-handler/domain tests;
3. audit every remaining new-entry path, including legacy Mobile/API/sync paths;
4. prove new Water/Electricity Metered assessment is rejected server-side;
5. prove historical Metered evidence remains unchanged/readable;
6. preserve IA-054 WCF direct-Mobile behavior.

## Local validation note
A focused Utilities unit-test run was attempted locally, but the running local API process (PID 21812 at the time) locked Api/bin Debug DLLs.
The build therefore failed on MSB3021/MSB3027 file-copy locks, not on a compiler/test assertion from these changes.
Domain and Application assemblies compiled before the lock was reached.
Cloud should rerun clean tests in its own environment.
## Confirmed utility rule
This is NOT an open product question.

Current Cantilan NEW activity:
- WCF = Cash Ticket + Direct Amount.
- ECF = Official Receipt + Direct Approved Amount.
- No new meter-reading, kWh, cubic-meter, or per-unit-rate assessment path.
- Historical Metered rows remain historical evidence and must not be repriced, erased, or auto-converted.
- Office-prepared WCF amount takes precedence; if none exists, authorized Collector Mobile may enter the direct amount.
- One-time WCF operation activation is prospective; no per-payor/per-month activation.

## Second primary task after utilities
Build a unified backend Collection Activity read model:
- authoritative legacy activity
- authoritative canonical posted Collections
- exactly once using CollectionSourceAuthorityMap / established authority logic
- no blind concat
- canonical BusinessDate
- actual document/instrument
- collector/source/classification details
- tenant-scoped
- itemized OR appears as one collection/document activity event with line detail
- correction semantics must follow existing reporting rules
- add PostgreSQL integration proof for duplicate prevention.
## Lane ownership for this branch
This is the BACKEND closure lane.
Owned:
- Domain
- Application
- API
- Infrastructure
- persistence/migrations only if truly necessary
- financial/read-model authority
- backend authorization/tests
- Mobile SERVER contracts only

Do NOT edit Web Razor/CSS/wwwroot or Mobile UI.
If frontend changes are needed, return an explicit FRONTEND CONTRACT FOLLOW-UP.

## Release safety
Do NOT:
- merge to master
- deploy
- apply production migrations
- cut over production sources
- backfill historical money
- publish APK
- version-bump
- force-push

## Remaining audits after the two primary fixes
Audit, do not invent:
- Fish/Meat Vendor Fee Mobile writer
- Kanmanggay Mobile writer
- Fiesta/Araw Mobile writer
- ECF Mobile intent/legacy path
- Slaughterhouse packages/add-ons
- annual targets governance
- seven `STALLTRACK_SNAPSHOT_DB` gated integration tests
## First action in Cloud
Run:
- git status
- git log -10 --oneline
- git show --stat 03809525
- inspect the six changed utility files

Then continue the utility slice from the first missing handler/domain/API/Mobile-server test.

Do not restart completed UI work.
Do not squash or discard 03809525 unless there is a demonstrated defect; amend/follow-up commits are fine.

After utilities are green, implement the unified Collection Activity backend feed and exactly-once tests.

At the end return the full "STALLTRACK V3 — RELEASE-CANDIDATE BACKEND CLOSURE CHECKPOINT" with exact test/build results and genuine remaining release gaps only.
