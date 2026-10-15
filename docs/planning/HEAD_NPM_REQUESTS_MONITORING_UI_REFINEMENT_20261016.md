# Head NPM Requests — Monitoring Navigation and Review UI
Date: 2026-10-16

## Scope
Head Portal only. Add NPM requests as a first-class Monitoring register and refine the collector-submitted NPM registration request queue and Head review drawer. Isolated local worktree `C:\dev\stalltrack-v2-worktrees\mobile-native-records-npm-polish-20261015`, branch `fix/mobile-native-records-npm-polish-20261015`. Protected primary checkout and parallel work must remain untouched.

## Root cause
The route `/monitoring/npm-requests` existed and Direct Collection Review linked to it, but the shared `MonitoringTabs` contained only Follow-ups and Contracts, making the registration queue undiscoverable from Monitoring. The NPM page used a plain table and a verbose drawer containing technical identifiers, duplicate financial disclaimers, raw `FishSection` enum labels, and warnings about same-number stalls in *unrelated* NPM sections. Those visual elements were inconsistent with the Follow-up Queue and Contracts Monitoring workspaces.

## Changes
- Shared MonitoringTabs now link **Follow-ups / Contracts / NPM requests** with proper active state and horizontally scrollable small-width layout. Existing past-period navigation preserved where enabled. No new sidebar section.
- NPM requests page uses the shared V3 civic-blue hero and the same Monitoring tab pattern, followed by a tidy action bar (**NPM register**, **Refresh**) and neutral table card.
- Table shows vendor, tax year, proposed stall, human-readable section, collector, date, office-review status and one **Review** action. **Submitted** is displayed as **Pending review**, **Returned** as **For correction**, and **Cancelled** remains a distinct historical status.
- Drawer begins with applicant, status, proposal and submission time. Shows collector and entered office reference; hides the prominent raw database GUID and repeated explanatory text. The compact, clearly worded **No cash received** notice is retained.
- Office-review section offers verified occupancy choice, office reason/evidence, and preserved **Return for correction / Reject / Approve registration** actions, still restricted to SuperAdmin. The Head must first create or link the actual verified NPM tenancy and Fish/Meat association. No new collection, charge, SRC, OR, or money.
- Duplicate-review warnings are limited to same-number **within the proposed section** and/or actual vendor identity matches, not coincidental Stall 2 in another area. If candidates are present, a clear warning remains with optional expandable evidence.
- Errors remain visible and fail closed; request review and submission behavior is unchanged.
- An existing component regression test was aligned to the shorter **Review** action and new Monitoring navigation tests added.

## Safety
- No schema changes or migrations are necessary for these display changes.
- No live NPM request was reviewed, cancelled, approved or otherwise modified as part of this UI refinement.
- No API, collector Mobile code, published APK, remittance, or revenue logic was changed.
- Head client Debug build passed with 0 errors; complete Head component suite passed **1,224 / 1,224**, including the Monitoring nav and NPM review decision path.
- The local Head Portal was safely restarted on **https://localhost:7567** using the same local API HTTPS 7597 and existing PostgreSQL configuration. Anonymous request checks: `/login` HTTP 200, protected `/monitoring/npm-requests` HTTP 302 to login (expected). Authenticated screenshot acceptance remains a manual check.
- The Windows Mobile preview and API were not restarted. Eight protected files in the primary checkout remain untouched.
