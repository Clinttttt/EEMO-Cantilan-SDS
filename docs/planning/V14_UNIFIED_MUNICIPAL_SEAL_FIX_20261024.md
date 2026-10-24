# V14 Unified Municipality Seal Presentation — October 24, 2026

## Scope and evidence
- Production Carrascal public branding returned the correct tenant and an HTTPS, versioned seal URL.
- The Carrascal image endpoint returned 200, image/jpeg, 54,362 bytes and Cache-Control public,max-age=31536000.
- A separate actual image decode succeeded: valid JPEG, 545 x 540 pixels. Neither missing upload nor corrupt image was evidenced.
- Production user screenshots showed a broken image on scoped Carrascal login and a neutral/blank sidebar on navigation.
- Source contained independent login and sidebar image render/error handling; sidebar navigation cleared failed URL tracking and repeated branding notifications reset recovery state.
- Reproduced the broken seal in fresh headless Microsoft Edge. Browser NetLog showed an embedded `https://api:8080/api/municipalities/carrascal/seal?v=...` request. The internal Docker host is not browser-reachable, although the public `https://api.stalltrack.site/...` seal responds 200. This is the confirmed root cause of the failed image URL, in addition to the sidebar reset/flicker lifecycle defects.

## Focused fix
- Add a production-configured `PublicApiBaseUrl` for API-generated embedded seal URLs. Both public/authenticated municipality branding and Head activation context now generate a browser-reachable HTTPS URL even when the portal calls the API through `api:8080`. Local environments without this override retain existing request-origin behavior. The API remains authoritative for versioned image URLs and no stored seal changes.
- Introduce shared `MunicipalSeal` presentation for Login, AuthBrandPanel and Sidebar; keep the existing layouts.
- Keep one neutral municipal-hall outline underneath a candidate seal image, with no borrowed municipality/vendor mark.
- On image failure remove the broken image and keep a neutral slot; retry only after a different source/version, not on each rerender.
- Preserve Sidebar's authenticated, tenant-scoped Office Profile fallback; never guess another LGU's identity.
- Stop resetting failure/fallback state on ordinary navigation or duplicate branding notifications, but invalidate it on a genuine tenant/URL change.
- For explicit and remembered non-default LGU login identity, a failed branding lookup remains neutral to that LGU rather than showing Cantilan.
- Retain the existing API, immutable image URLs, browser caching, authentication, authorization, reporting, and financial data behavior.

## Verification
- API SealUrl tests cover an internal Docker request emitting a public HTTPS URL, separate tenant/version URLs, and existing local/nonembedded behavior.
- Focused component tests cover healthy seal URLs, failed-source neutral fallback, no repeat retry, version changes, tenant switching, login identity and authenticated sidebar fallback.
- Focused Head suite rerun: 65/65 passed (2026-10-24).
- Full Head component suite repeat: **1,452/1,452 passed**. An earlier run surfaced outdated branding assertions and an unrelated intermittent collection-manager event-binding failure; the amended branding assertions and full rerun passed. No collection-manager business code or assertions were changed.
- Full Unit suite: **3,147/3,147 passed**, including all four new SealUrl tests.
- Existing NU1902 warning: AngleSharp 1.2.0 has a moderate-security advisory; package upgrade is a separate dependency-maintenance task.
- Run the Head component suite separately from other test projects; do not treat compilation as visual/browser acceptance.
- No database migrations, financial changes, account changes, or infrastructure changes.

## Release gate
- Inspect actual browser network errors and visual login/navigation/logout behavior on at least Carrascal and Cantilan.
- Explicit scope for future production promotion is separate from this local implementation. Record merged SHA and immutable release before asserting production success.
