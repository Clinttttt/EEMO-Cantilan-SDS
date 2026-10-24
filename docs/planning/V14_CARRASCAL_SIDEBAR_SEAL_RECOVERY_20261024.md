# V14 Carrascal sidebar logo recovery — October 24, 2026

## Reproduction evidence and limits
- Head Portal Office & LGU Profile screenshot shows the stored Carrascal seal, while the sidebar remains on the neutral municipal-hall placeholder. This is not a missing upload.
- Public `GET https://api.stalltrack.site/api/municipalities/carrascal/branding` returned HTTP 200, Carrascal/CLEE, `https://api.stalltrack.site/api/municipalities/carrascal/seal?v=84b758dc31db`.
- A separate read-only GET of that seal returned HTTP 200, `image/jpeg` with 54,362 bytes. The portal document CSP only declares `frame-ancestors 'self'`, so no evidence currently establishes CSP blocking.
- Source trace: Office Profile previews `GetOfficeProfileAsync().SealPath` (stored data URI), but Sidebar uses `BrandingState.SealPath` from the separately authenticated current-branding endpoint and falls back to the placeholder when the read is unavailable or the image fires `onerror`.
- Authenticated live browser network/circuit diagnostics were unavailable; whether initial branding GET failed, was delayed, or its rendered image failed is not directly confirmed. This implementation covers both fallback paths without falsely blaming the data or replacing it.

## Scoped implementation
- Existing authoritative branding endpoint and versioned seal URL remain first choice.
- Only if primary identity/image cannot render, the sidebar asks the **already-authenticated tenant-scoped Office Profile API** for its stored PNG/JPEG/WebP data URI. It never searches public tenants using guessed identifiers, never adopts a default LGU's seal, and uses a neutral placeholder if no supported own image is available.
- Handles initial branding failure, post-connection authentication availability, image error, and refresh; avoids stale in-flight fallback responses overriding newer branding. Prevents fallback-image error loops.
- No redesign or styling changes. No financial or tenant policy changes. Only `Sidebar.razor` and a dedicated bUnit test fixture were altered.

## Verification
- Public endpoint and JPEG independently verified (HTTP 200, as above).
- Five new component tests: primary URL preferred/no extra lookup; broken URL fallback to own profile; branding read failure recovers; denied own-profile fallback stays neutral; bad fallback image cannot loop.
- Focused Sidebar/Office Profile/prerender/navigation suite: 50 passed, 0 failed.
- First full Head suite: 1,444 passed, 1 unrelated MonthlyCollectionReportPageTests filter-selection failure; failing test passed alone (1/1). Full repeat: **1,445 passed, 0 failed/skipped**.
- Git whitespace diff check clean, source compiles as part of Head tests. Live authenticated Carrascal render and browser DevTools network observation still needed after deployment. Do not claim this is already live.

## Operational scope
- Isolated worktree `C:\dev\stalltrack\v14-sidebar-seal-recovery-20261024`, branch `fix/v14-sidebar-seal-recovery-20261024`, based on latest production source `8188b685`.
- No production deployment, billing mutations, database migration or upload/rewrite occurred.
