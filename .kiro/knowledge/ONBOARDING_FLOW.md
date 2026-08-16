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

As of 2026-08-16: **Cantilan is Active. Carrascal, Carmen, Lanuza and Madrid are `Upcoming` placeholders** with every field empty —
the shape of a municipality that has never been onboarded. **Landing on `/login` is NOT evidence that a municipality holds data**;
the page rendered a form for any `?lgu=` code before this was fixed.

Whether an unactivated LGU has any *accounts* cannot be told from outside: sign-in answers a bare `401`, which correctly refuses
to distinguish "no such account" from "wrong password". Answering that needs database access.

## Branding on a page reached before sign-in

The office's rule, confirmed 2026-08-16:

- **Cantilan's seal, name and office are the sanctioned fallback.** It is the office this system belongs to, and its values are the
  default rather than a stray hardcoded string.
- **A municipality's seal slot is KEPT but never borrowed.** Onboarding is where an LGU uploads its seal, so the card stays for it
  to fill; until then it shows a plain waiting slot. It used to show StallTrack's own seal with `alt="{Municipality} seal"` — the
  product's mark twice over, one of them describing itself to a screen reader as a municipal seal.
- **A tenant code must never decide anything in the portal.** Branding defaults are cosmetic; a decision keyed on a tenant string
  grants or withholds. See `BackupsOperatorDecisionTests`.

The same borrowed-seal fallback still exists in `AccountSetup`, `ForgotPassword`, `ResetPassword`, `VerifyEmail` and
`BrandingState` (which serves the signed-in portal). **Not yet fixed.**

## Links belong in one place

| Class | Gives | Environment variable |
|---|---|---|
| `OnboardingLinks` | `Base`, `Build(token)` — the workspace link the operator e-mails | `ONBOARDING_LINK_BASE` |
| `LandingSiteLinks` | `Base`, `MunicipalityPage(code)` | `LANDING_SITE_BASE` |

Both fall back to the production domain when unset, so existing deployments are unchanged. `MunicipalityPage` lower-cases the code,
because the landing site's own links are written that way (`?lgu=carrascal`) and a casing its router does not match would fall
through the same wildcard that caused the original fault.
