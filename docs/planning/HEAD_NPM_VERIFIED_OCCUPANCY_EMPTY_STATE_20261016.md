# Head NPM request — verified occupancy empty-state refinement
Date: October 16, 2026

## Issue
The request-review drawer showed an openable `Verified occupancy` dropdown that contained only the placeholder **Select a verified NPM occupancy**, even where no verified Fish/Meat-to-NPM association exists. This implied that a choice was available when none was recorded.

## Resolution
- Only render the verified-association selector when a returned active NPM occupancy has a current association matching the request's Fish/Meat vendor registration.
- Otherwise show **No verified NPM occupancy linked**, with a brief instruction, **Open NPM register** (in another tab), and **Check again** to refresh verified associations without losing the pending request review. Loading and API failure states remain distinct; retry is available on failure.
- Keep approval disabled unless a real association GUID is explicitly selected and the office review reason is filled.
- No automatic approval, no occupancy created from the proposed stall, no money, SRC, OR, or collector receipt.

## Scope and checks
Pure Head UI and component-test change. No API, database or migration change; no user requests were approved or rejected. Full Head component suite: **1,225 passed, 0 failed**. Head Portal HTTP 200 at `https://localhost:7567/login`; protected NPM route redirects unauthenticated requests to login. Local API `https://localhost:7597/health` HTTP 200. Worktree is isolated from the eight protected dirty files in `C:\dev\stalltrack\eemo`. Authenticated browser acceptance is for the Head to review.
