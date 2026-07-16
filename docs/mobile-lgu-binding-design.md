# Mobile Per-LGU App Binding — Design Sketch

**Status:** Planning (not yet implemented)
**Owner:** EEMO / StallTrack
**Last updated:** 2026-07-16

## Goal

Give every LGU (municipality) a mobile collector app that *feels* like their own — the
collector opens the app and it is **already their LGU**: their seal, office name, and
municipality line, with **no "Select Municipality" picker**. Delivered by a link the Head
shares, without publishing a separate app per LGU.

## Overriding invariant

**Cantilan (default/golden tenant) never breaks; the monthly-rental online-payment path stays
byte-for-byte unchanged.** Any binding logic must degrade to today's behaviour when no LGU is
bound (fallback picker / Cantilan defaults).

---

## Key design decision: one binary, tenant bound after install

A mobile app is a single compiled binary — you cannot bake a different logo/tenant into a
*download link*. Two ways to get per-LGU branding:

- **Separate app per LGU** — a distinct Play Store / App Store submission, review, and update
  cycle per municipality. Does not scale. **Rejected.**
- **One generic app, tenant bound via a link/token** (the SaaS-workspace pattern). The app is
  generic; a **bind link** tells it which LGU it is on first open, then it pulls that LGU's
  branding from the API. **Chosen.**

The per-LGU identity is **bound after install**, not baked into the download. The user never
sees the seam — the result is exactly the "owns their app" feel.

The branding itself is **already data-driven** (audit fix #3): the seal/office/municipality are
stored on `Municipality` and served by the API, so once the app knows its LGU it renders that
identity automatically.

---

## Distribution model

- **One generic APK**, hosted (Azure Blob or static site), stable URL, e.g.
  `https://app.stalltrack.site/download/stalltrack-collector-latest.apk`.
- Two links exist:
  - **Download link** — the APK; same file for everyone; downloading alone does nothing useful.
  - **Bind link** (LGU-scoped) — `https://app.stalltrack.site/a/{bindToken}` — makes the app
    "theirs."
- **The Head distributes the bind link** to their collectors.
- **"Only their collectors can use it"** is enforced by *accounts*, not link secrecy: a person
  can download the APK and open a bind link, but cannot log in without a collector account the
  Head created in that LGU. The real gate is the login + LGU-scoped account (unchanged from
  today).

### Android "unknown sources" note

A hosted APK requires collectors to enable "Install from unknown sources" once — a small
trust/friction step. The Play Store avoids that but costs the per-app overhead above. For a
controlled collector rollout, hosted APK is acceptable for v1.

### iOS note

Hosted distribution does not work on iOS (requires App Store / TestFlight). The app is
Android-first; treat iOS as a later, separate path.

---

## The bind token (LGU-scoped)

- Add an opaque `MobileBindToken` to the `Municipality` entity.
- Generated at activation (`ActivateMunicipalityCommandHandler`), rotatable if leaked.
- The activation email to the Head includes their LGU's bind link; the Head shares it with
  their collectors.
- **Later:** mint per-collector bind links when the Head creates a collector (cleanest — binds
  the app *and* pre-fills the account). v1 can use one link per LGU.

---

## API: bind endpoint (anonymous, token-resolved)

```
GET /api/mobile/bind/{bindToken}
 → 200 { municipalityCode, tenantCode,
         branding { name, province, officeName, officeAcronym, sealPath } }
 → 404 (unknown / rotated token)
```

Reuses the same branding data as `GetCurrentMunicipalityBranding`, resolved by the bind token
**pre-login**. Purely presentation + login-scoping; see the security model below.

---

## Deep link + static landing page

- **Android App Link**: `https://app.stalltrack.site/a/{token}` opens the app if installed; if
  not, lands on a small **static page** ("Download the app" + instructions). After install, the
  same link opens the app and binds it.
- The static page hosts `/.well-known/assetlinks.json` (authorizes the app to handle the
  domain's links) — required for tap-link-to-open-app.
- Optional custom-scheme fallback: `stalltrack://a/{token}`.

---

## Mobile: store + use the bound LGU

- On bind, store `boundMunicipalityCode` (+ cache branding) in device preferences.
- **Login screen:**
  - Bound code exists → **hide the picker**, show the LGU logo/name, silently send the code on
    login (also satisfies the #5 fail-closed guard).
  - No bound code → "Paste your activation link" prompt or the existing picker as fallback
    (preserves Cantilan / single-tenant behaviour).
- Keep the binding across logout (stays "their app"); clear only on an explicit "switch LGU."

---

## Security model

- The bind link is **not** the security boundary. Binding is presentation + login-scoping only.
- Real isolation stays server-side: JWT tenant claim + query filters + LGU-scoped collector
  accounts — unchanged. A mis-bound app still cannot read another LGU's data.
- Bind token is LGU-scoped and rotatable. (Optionally add an expiry for per-collector links.)

---

## CI/CD + "auto-update" (reality check)

Two separate ideas:

- **CI/CD** — automate build+publish. Add a GitHub Actions job (same pattern as the web/api
  container deploy): on release, run `dotnet publish -f net10.0-android`, sign the APK, upload
  to the hosted URL (`-latest.apk` + a versioned copy). A code change → a fresh APK is published.
- **Auto-update** — a hosted APK does **not** silently auto-update like the Play Store (Android
  blocks silent installs for side-loaded apps). "Auto-update" here = an **in-app update check**:
  on launch the app calls `GET /api/mobile/version` → `{ latestVersion, apkUrl, mandatory }`; if
  the installed version is older, it prompts "Update available → Download" and the user taps to
  install. Prompted, not invisible. True silent auto-update requires the Play Store.

---

## Already done vs. to build

**Done**
- Data-driven mobile branding (seal/office/name) — audit fix #3.
- Tenant-scoped login + fail-closed on ambiguity — audit fix #5.

**To build**
- `MobileBindToken` on `Municipality` + generate at activation + include in the Head's email.
- `GET /api/mobile/bind/{token}` endpoint.
- Mobile: deep-link handler, store bound code, hide picker + auto-send code.
- Static landing page (download + `assetlinks.json`).
- (Optional now) `GET /api/mobile/version` + in-app update prompt.
- (Later) CI/CD job to build/sign/publish the APK; per-collector bind links.

---

## Suggested phasing

- **v1 (core "owns their app" feel):** bind token + bind endpoint + mobile deep-link/no-picker +
  branding (done) + static download page. Manual APK builds are fine at first.
- **v2:** CI/CD APK publishing + in-app update prompt (`/version`).
- **v3:** per-collector bind links tied to collector provisioning.

## Open decisions

- **Who mints/rotates the bind token** — proposed: a "Copy collector-app link" action in the
  Head's web portal (re-share/rotate), plus the initial link in the activation email.
- Bind-link granularity for v1: **one per LGU** (simple) vs **per collector** (cleaner). Leaning
  one-per-LGU for v1.
