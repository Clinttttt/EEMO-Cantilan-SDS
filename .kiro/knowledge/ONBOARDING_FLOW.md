# How a municipality joins StallTrack

Written 2026-08-16, after a redirect was pointed at the wrong step and landed the office on a marketing page. Everything below was
verified against the running system — the landing site's own route table, the API's controllers, and live responses — not from
memory. **If a change touches any step here, re-verify rather than trusting this file; two backlog notes have already gone stale.**

## The two sites, and why the difference matters

| | Address | Lives in | What it is |
|---|---|---|---|
| **Landing site** | `www.stalltrack.site` | **NOT this repository** | Public Angular SPA on Cloudflare. The product pages, the municipality selector, and the onboarding workspace. |
| **Console** | `console.stalltrack.site` | `EEMOCantilanSDS.Client` | The office's portal. Sign-in and everything after it. |
| **API** | `stalltrack-api-…azurewebsites.net` | `EEMOCantilanSDS.Api` | Serves both. |

The landing site cannot be inspected or changed from this repository. Its routes were read from its own JS bundle:

```
''      '**'      'ai-roadmap'   'assessment-received'   'municipalities'   'municipalities/:code'
'onboarding/:token'   'privacy'   'select-municipality'   'terms'   'thanks'
```

**`onboarding` exists ONLY as `onboarding/:token`.** There is no token-less onboarding page. A redirect to
`https://www.stalltrack.site/onboarding` falls through the `'**'` wildcard and renders the marketing home page — which is exactly
the fault this document exists to prevent repeating.

## The order

| # | Step | Where | Who | Endpoint |
|---|---|---|---|---|
| 1 | Choose a municipality | `select-municipality` | anyone | — |
| 2 | See its status, request an assessment | `municipalities/:code` | the LGU | `POST /api/assessment/requests` · **anonymous** |
| 3 | Acknowledgement | `assessment-received`, `thanks` | — | — |
| 4 | Review the requests | console | operator | `GET /api/assessment/requests` · SuperAdmin |
| 5 | Approve or decline | console | operator | `POST /api/assessment/requests/{id}/approve` · `/decline` |
| 6 | Fill in the workspace | `onboarding/:token` | the LGU | `GET`/`PUT /api/onboarding/{token}`, `POST /api/onboarding/{token}/submit` · **anonymous, the token IS the authority** |
| 7 | Validate the submission | console | operator | `POST /api/onboarding/by-request/{id}/approve-validation`, `/return` |
| 8 | Activate | console | operator | `ActivateMunicipalityCommandHandler` |
| 9 | Sign in | `console…/login?lgu={code}` | the office | — |

The token link for step 6 is built by `OnboardingLinks.Build(token)` and **e-mailed** by the operator. That is the only correct use
of `OnboardingLinks.Base`.

## Where an unactivated municipality belongs

**Step 2 — its own page**, `municipalities/{code}`, built by `LandingSiteLinks.MunicipalityPage(code)`. That is where its status
is shown and where the assessment request is made.

`Login.razor` sends it there when branding reports `IsActive == false`. Three details in that code are load-bearing:

- **The redirect sits OUTSIDE the branding `try/catch`.** `NavigateTo` signals a redirect during static rendering by *throwing*
  `NavigationException`, so a redirect inside that `catch` is swallowed and silently does nothing. The first version did exactly
  that, built clean, read correctly, and served the sign-in form anyway.
- **Only the `?lgu=` path forwards anybody.** Typing a username must not: branding resolves from a half-typed name, and forwarding
  on `carrascal.h…` would throw somebody out of a login they were in the middle of.
- **A failed branding fetch never forwards.** The flag stays false, so this can never be the reason the office cannot reach its own
  sign-in.

## What each state looks like from outside

`GET /api/municipalities/{code}/branding` is anonymous, and answers for any municipality.

| | Never onboarded | Active |
|---|---|---|
| `status` | `Upcoming` | `Active` |
| `isActive` | `false` | `true` |
| `officeName` | `""` | e.g. `Economic Enterprise & Management Office` |
| `sealPath`, `officeAcronym`, `address`, `reportSignatories` | `null` | filled in during onboarding |

As of 2026-08-16, **verified directly against the production database**, not inferred: Cantilan is Active; **Carrascal, Carmen,
Lanuza and Madrid are untouched `Upcoming` placeholders.**

| Checked | Result |
|---|---|
| Every table carrying `MunicipalityId` (33 of them) | rows exist for **CANTILAN only** — 72 audit logs, 8 facilities, 6 stalls, 6 contracts, 6 rates, 4 payment records, 3 daily collections, 2 users, 1 each of device token, facility assignment, utility bill, tenant backup |
| Every other municipality, every table | **0 rows** |
| `AssessmentRequests` | **0 rows** (the whole table) |
| `OnboardingDrafts` | **0 rows** (the whole table) |
| The four placeholder rows themselves | `Status = 0` (Upcoming), `IsActive = false`, `IsDeleted = false`, empty `OfficeName`, and `NULL` acronym, address, seal, market day, PayMongo keys and secrets, signatories, bind token |

**So nothing needed deleting.** The office asked for the non-Cantilan data to be cleared so onboarding could be tested from the
beginning; the database was already in exactly that state. A cleanup was prepared and then not run, because looking first showed
there was nothing to remove — which is the reason to look first.

Two things that reading the database settled, which no amount of poking from outside could:

- **Cantilan's own `SealPath` is NULL.** Its seal on screen comes from the bundled `LGU_CANTILAN_LOGO.jpg` fallback, not from the
  database. Uploading a logo through Office Profile would populate the column; until then the fallback is what shows. Not a defect,
  but worth knowing before anybody "fixes" a seal that is already correct.
- **No accounts exist for the four.** Sign-in answers a bare 401 whether an account is missing or the password is wrong, so this
  could only ever be answered from the database side.

**Landing on `/login` is NOT evidence that a municipality holds data**; the page rendered a form for any `?lgu=` code before this
was fixed.

Whether an unactivated LGU has accounts cannot be told from outside: sign-in answers a bare `401`, which correctly refuses to
distinguish "no such account" from "wrong password". Answering that needs database access — see the note below on how.

## Reaching the database, when it is genuinely necessary

The connection string lives in the API app's settings (`ConnectionStrings__DefaultConnection`), readable with `az webapp config
appsettings list`. The Postgres firewall does **not** allow arbitrary clients: it carries
`AllowAllAzureServicesAndResourcesWithinAzureIps` (how the API and the backup workflow connect) plus a couple of stale client-IP
rules. A one-off inspection therefore needs a temporary rule for the current address.

The sequence used on 2026-08-16, and the one to repeat:

1. **Take a fresh backup first** — `gh workflow run backup.yml`, and wait for `success`.
2. Add a firewall rule for the current IP, named so it is obviously temporary.
3. Inspect. `psql` via the `postgres:17-alpine` container avoids installing anything.
4. Change nothing until the counts are on screen.
5. **Remove the temporary rule**, and delete any local file holding the connection string.

Two stale client-IP rules (`180.194.5.178`, `180.195.158.234`) remain open indefinitely and belong to no current machine. Removing
them is the office's call, but they are standing exposure for no benefit.

## Branding on a page reached before sign-in

The office's rule, confirmed 2026-08-16:

- **Cantilan's seal, name and office are the sanctioned fallback.** It is the office this system belongs to, and its values are the
  default rather than a stray hardcoded string.
- **A municipality's seal slot is KEPT but never borrowed.** Onboarding is where an LGU uploads its seal, so the card stays for it
  to fill; until then it shows a plain waiting slot. It used to show StallTrack's own seal with `alt="{Municipality} seal"` — the
  product's mark twice over, one of them describing itself to a screen reader as a municipal seal.
- **A tenant code must never decide anything in the portal.** Branding defaults are cosmetic; a decision keyed on a tenant string
  grants or withholds. See `BackupsOperatorDecisionTests`.

The same borrowed-seal fallback existed in five more places and is now **FIXED across all of them (2026-08-17)**. The full set was three
components that render a municipal seal — `Login` (fixed first), the shared `AuthBrandPanel` behind forgot-password, reset-password and
verify-email, and `AccountSetup` — plus `BrandingState`, which serves the signed-in portal. `AdminActivate` was checked and renders only
the StallTrack card, so it had nothing to borrow.

- **`BrandingState` was the serious one.** Its `SealPath` is rendered at **31 places in 22 files, most of them PRINTED** — official
  reports, the collection receipt, the stallholder list, a payor's history. An LGU with no seal on file was **issuing documents carrying
  the vendor's emblem, labelled as its own seal**. A private company's mark does not belong on a government document.
  - Fixed at the source rather than across 22 files: the fallback is now `WaitingSealPath`, a faint municipal-hall outline inline as a
    data URI. Every render site is correct without being touched, no asset was added, and it scales cleanly in print because it is
    vector. `HasOwnSeal` is exposed for any screen that would rather omit the seal than show a placeholder.
- The placeholder rule moved from `Login.razor.css` into **app.css**, because three components need it and Blazor's scoped CSS never
  crosses a component. Keeping a copy per stylesheet is how the four existing copies of `.government-logo-card` came to exist, and how
  they will drift.
- Cantilan is unaffected throughout: it keeps its own seal, as the office this system belongs to.
- Verified locally on each screen — Cantilan keeps its seal, and forgot-password, reset-password and verify-email show the waiting slot
  for an LGU without one. `/account-setup-admin` could not be seen rendered because that route 302s once setup is complete; its fix is
  in place for a genuine first-time setup.
- `LoginSealTests` now asserts across all three markup files AND against `BrandingState`, each proven by reintroducing the fault.

## What lives on the landing site, and therefore cannot be changed from here

Requests about the following belong to the landing site's own repository, not this one. Recorded so they are not lost, and so
nobody looks for them in this codebase:

- **"Carrascal is live on StallTrack / Enter the official portal … / Enter Portal"** on `municipalities/:code` — reported as
  redundant, to be removed.
- **The StallTrack seal and the product footer** on the same page (the "A GovTech SaaS platform…" blurb, Product / Features /
  AI Roadmap / Use Cases / Product Preview / Security / Company / Founder / Contact / Privacy Policy / Terms of Service) — also to
  be removed.
- **The selector shows every municipality as "Active"**, including the four that are `Upcoming` and inactive according to
  `GET /api/municipalities`. Its badges do not reflect the API. The same page also says an unactivated municipality is "live",
  which is what led the office to expect Carrascal to have a portal at all.

## What a market section is, and who names it

A public-market daily sheet is organised into three collection areas. The platform keys everything on the three
`MarketSection` values (`VegetableArea`, `FishSection`, `MeatSection`); the LGU supplies its own NAME for each area, in
its own language, and that name is display only. Both sides travel end to end:

| Where | Carries |
|---|---|
| Landing onboarding (`onboarding-workspace.ts`) | each section's `kind` (declared by the LGU) + `name` (its own words) |
| Admin console (`market-sections.ts`, `activation.mapper.ts`) | reads `kind`; sends `sectionLabels` on the NPM facility and derives `NpmFishPerKilo` from the declared fish area |
| API (`ActivateMunicipalityCommand`) | `ActivationFacility.SectionLabels` → `Facility.SetSectionLabels` |
| Portal (`FacilityState.SectionLabelOf`) | the LGU's label, or the canonical wording when it named none |

The `kind` values are the `MarketSection` names verbatim, in all three codebases, so no translation table exists to
disagree with itself.

**Nothing reads meaning out of a section's wording.** Until 2026-08-20 it did: the console decided the fish area by
`/fish/` on the section name and the weighing fee by `/kilo|fish/` on the fee's own name, and the API classified by
English keyword and took whatever was left as the vegetable area. Madrid entered **Gulayan, Isda, Karne** and so:
its fish and meat areas were dropped and rendered as "Fish Area" / "Meat Area", and — the part that costs money —
its per-kilo weighing fee was never offered in the form, so no `NpmFishPerKilo` rate was seeded at all. An area an
LGU has not named keeps the canonical wording, which states nothing about anybody, and its Head can set the name in
the facility Configuration drawer. A draft saved before the question was asked has its areas filled in the order the
LGU entered them — on screen, before the operator commits, and warned about in the mapper's `warnings`.

## Links belong in one place

| Class | Gives | Environment variable |
|---|---|---|
| `OnboardingLinks` | `Base`, `Build(token)` — the workspace link the operator e-mails | `ONBOARDING_LINK_BASE` |
| `LandingSiteLinks` | `Base`, `MunicipalityPage(code)` | `LANDING_SITE_BASE` |

Both fall back to the production domain when unset, so existing deployments are unchanged. `MunicipalityPage` lower-cases the code,
because the landing site's own links are written that way (`?lgu=carrascal`) and a casing its router does not match would fall
through the same wildcard that caused the original fault.
