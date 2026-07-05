# StallTrack — LGU Activation API: Request Contract & Integration Guide

**Audience:** the `stalltrack-platform` (admin console) team.
**Status:** live. This documents the deployed Phase 6 activation endpoint.
**Endpoint owner:** StallTrack backend (`EEMOCantilanSDS.Api`).

This is the API the **Activation** step of the onboarding flow calls to turn a staged LGU configuration
into a live, isolated municipality. It replaces the demo's local-state "go live" action.

---

## 1. Endpoint

```
POST /api/activation/municipality
Content-Type: application/json
Authorization: Bearer <access-token>
```

- **Base URL (prod):** `https://stalltrack-api-clint-2026-g9awcrbed5fdakh8.southeastasia-01.azurewebsites.net`
- **Full URL:** `{base}/api/activation/municipality`

### Authorization (who may call it)
The caller must present a valid access token for a **SuperAdmin of the default municipality (Cantilan)** —
i.e. the **platform operator**. This is enforced twice: `[Authorize(Roles=SuperAdmin)]` on the endpoint and a
default-LGU check in the handler. A per-LGU Head (any non-default municipality) is rejected with `403`.

> Obtain the token via the normal admin login (`POST /api/adminauth/login`) as the Cantilan Head, then send it
> as `Authorization: Bearer <accessToken>`. Access tokens expire in 15 minutes; refresh as usual.

---

## 2. Request body

All enums are sent as **strings** (case-insensitive). Property names are **camelCase**.

```jsonc
{
  "municipalityCode": "CARMEN",              // target LGU; must already exist as an "Upcoming" registry entry
  "branding": {
    "officeName": "Carmen Economic Enterprise Office",   // required
    "address": "Carmen, Surigao del Sur",                // optional (null allowed)
    "sealPath": null                                     // optional path/URL to the seal asset
  },
  "administrator": {                          // the single LGU owner (Head / SuperAdmin) provisioned now
    "fullName": "Maria Santos",               // required
    "username": "carmen.head",                // required; unique within this LGU
    "email": "head@carmen.gov.ph"             // required; must be a valid email
  },
  "facilities": [                             // >= 1 required
    { "code": "NPM", "name": "Carmen Public Market", "shortName": "CPM", "archetype": "DailyStall",
      "stallGroups": [                        // OPTIONAL — provisions the facility's stalls/units (spaces)
        { "count": 40, "monthlyRate": 0, "dailyRate": 25.00, "fees": "DailyRental, FishFee", "section": "FishSection" },
        { "count": 30, "monthlyRate": 0, "dailyRate": 25.00, "fees": "DailyRental", "section": "MeatSection" }
      ] },
    { "code": "SLH", "name": "Carmen Slaughterhouse", "shortName": "CSLH", "archetype": "PerHead" }
  ],
  "rates": [                                  // fixed ordinance rates only (see §5); may be empty
    { "facilityCode": "NPM", "key": "NpmDailyStall",  "amount": 25.00 },
    { "facilityCode": "NPM", "key": "NpmFishPerKilo", "amount": 2.00 },
    { "facilityCode": "SLH", "key": "SlhHogPerHead",  "amount": 200.00 },
    { "facilityCode": "SLH", "key": "SlhLargePerHead","amount": 300.00 }
  ]
}
```

### Field reference

| Path | Type | Required | Notes |
|---|---|---|---|
| `municipalityCode` | string | ✅ | Registry code of the target LGU (upper-cased server-side). Must be an existing **Upcoming** LGU. |
| `branding.officeName` | string | ✅ | Revenue office label shown on reports/receipts. |
| `branding.address` | string? | — | LGU/office address. |
| `branding.sealPath` | string? | — | Path/URL to the municipal seal asset. |
| `administrator.fullName` | string | ✅ | Head's full name. |
| `administrator.username` | string | ✅ | Head login; unique **within the LGU** (same username may exist in another LGU). |
| `administrator.email` | string | ✅ | Valid email. |
| `facilities[]` | array | ✅ (≥1) | The LGU's facilities. One entry per `code` (a code is unique per LGU). |
| `facilities[].code` | `FacilityCode` | ✅ | See enum below. |
| `facilities[].name` | string | ✅ | Official display name. |
| `facilities[].shortName` | string | ✅ | Short label/acronym. |
| `facilities[].archetype` | `BillingArchetype` | ✅ | How it bills. |
| `facilities[].stallGroups[]` | array | — | Optional. Provisions the facility's stalls/units (spaces). Omit for transaction-only facilities (SLH/TRM/TPM). |
| `facilities[].stallGroups[].count` | int | ✅ | Number of stalls to create (1–5000). |
| `facilities[].stallGroups[].monthlyRate` | decimal | ✅ | Per-stall monthly rate (use for MonthlyRental; `0` for daily stalls). |
| `facilities[].stallGroups[].dailyRate` | decimal? | — | Per-stall daily rate (daily stalls); null falls back to the facility's `NpmDailyStall` rate. |
| `facilities[].stallGroups[].fees` | `ApplicableFees` | ✅ | Fee flags (comma-separated string). |
| `facilities[].stallGroups[].section` | `MarketSection`? | — | Market section (Fish/Meat/Vegetables) for public markets. |
| `rates[]` | array | ✅ (may be empty) | Fixed ordinance rates only. |
| `rates[].facilityCode` | `FacilityCode` | ✅ | Facility the rate belongs to. |
| `rates[].key` | `FeeRateKey` | ✅ | Which fixed rate. |
| `rates[].amount` | decimal | ✅ | ₱ amount; must be ≥ 0. |

### Enum values (send as strings)

- **`FacilityCode`**: `NPM`, `TCC`, `NCC`, `BBQ`, `ICE`, `SLH`, `TRM`, `TPM`
- **`BillingArchetype`**: `DailyStall`, `MonthlyRental`, `WeeklyMarket`, `PerTrip`, `PerHead`, `Custom`
- **`FeeRateKey`**: `NpmDailyStall`, `NpmFishPerKilo`, `SlhHogPerHead`, `SlhLargePerHead`, `TpmVendorDay`, `TrmPerTrip`
- **`ApplicableFees`** (flags — comma-separate): `BaseRental`, `DailyRental`, `Electricity`, `Water`, `FishFee` (or `None`)
- **`MarketSection`**: `VegetableArea`, `FishSection`, `MeatSection`

---

## 3. Success response — `200 OK`

```jsonc
{
  "municipalityId": "1c9f...-guid",
  "municipalityCode": "CARMEN",
  "adminUsername": "carmen.head",
  "temporaryPassword": "Aa1!K7x…",   // one-time; see §6
  "facilitiesCreated": 2,
  "ratesCreated": 4,
  "stallsCreated": 94
}
```

On success the LGU is **Active**, its facilities/rates exist under its own `MunicipalityId`, and its Head is
provisioned in a **must-change-password** state.

---

## 4. Error responses

| Status | When | Body |
|---|---|---|
| `400` | Validation failed | `{ "isSuccess": false, "errors": { "field": ["msg"] } }` |
| `400` | Business rule (target is the **default** LGU, target is **already Active**, or username already taken in the LGU) | `{ "isSuccess": false, "error": "…" }` |
| `401` | Missing/expired/invalid token | — |
| `403` | Token is not a SuperAdmin, or not the **default-LGU** operator | — |
| `404` | `municipalityCode` not found in the registry | — |

The commit is **atomic**: any error leaves the target LGU **Upcoming** with no partial data.

---

## 5. Mapping your onboarding config → this contract

The onboarding workspace captures a richer config; map it down to the contract as follows.

- **Each onboarding facility → one `facilities[]` entry**, mapped to a `FacilityCode` + `BillingArchetype`.
  A `FacilityCode` is **unique per LGU**, so map each facility to a distinct code.
- **Fixed ordinance rates → `rates[]`**, keyed by `FeeRateKey`:
  | Archetype | Facility (example) | Rate key(s) to send |
  |---|---|---|
  | `DailyStall` | NPM | `NpmDailyStall` (+ `NpmFishPerKilo` if a fish section) |
  | `PerHead` | SLH | `SlhHogPerHead`, `SlhLargePerHead` |
  | `WeeklyMarket` | TPM | `TpmVendorDay` |
  | `PerTrip` | TRM | `TrmPerTrip` |
  | `MonthlyRental` | TCC/NCC/BBQ/ICE | **none** — monthly rentals are per-stall (`Stall.MonthlyRate`), entered in the portal later |

- **Not part of activation (do NOT send):** sections/section-fees beyond the fish key, add-on fees
  (electricity/water, fixed or metered), payors/stallholders, collectors/admins, OR-series config. These are
  entered/created in the **live portal after activation** (payors, daily collections, staff) or are planned
  contract extensions (sections, metered add-ons, OR series). Send only what maps to the fields above.

---

## 6. Head account & first sign-in

- The response's `temporaryPassword` is generated server-side and returned **exactly once** (never stored in
  plaintext, never retrievable again).
- Convey it to the Head through your branded activation message. The Head signs in at the portal with
  `adminUsername` + the temporary password and is **forced to set a new password** on first login
  (`MustChangePassword`). After that they invite/create their own admins and collectors and begin entering
  payors/stalls in the portal.
- (Roadmap: a tokenized "set your password" link may replace the temporary password later; the contract above
  is unaffected.)

---

## 7. Operational notes

- **Idempotency:** calling activation for an already-Active LGU returns `400` (safe no-op). Only call once per
  LGU; treat `400 "already active"` as "already done".
- **Targets available today:** the registry seeds `CARRASCAL`, `MADRID`, `CARMEN`, `LANUZA` as Upcoming.
  `CANTILAN` is the default operator LGU and can never be a target (`400`).
- **Isolation:** everything created is scoped by `MunicipalityId`; the new LGU's users see only their own
  facilities/rates/data, and their fees compute from the rates you send here.
- **Antiforgery / CORS:** antiforgery is **not** globally enforced, so a `Bearer`-authed POST is accepted with
  no CSRF token. Prefer calling this endpoint **server-side from the platform backend** (keeps the operator's
  token off the browser and sidesteps CORS). If you must call it from the browser, the API's CORS policy must
  allow the platform origin — coordinate with the backend team first.

---

## 8. Example (curl)

```bash
# 1) Operator (Cantilan Head) logs in
TOKEN=$(curl -s -X POST "$BASE/api/adminauth/login" \
  -H "Content-Type: application/json" \
  -d '{"username":"<cantilan-head>","password":"<password>"}' | jq -r .accessToken)

# 2) Activate Carmen
curl -X POST "$BASE/api/activation/municipality" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "municipalityCode":"CARMEN",
    "branding":{"officeName":"Carmen Economic Enterprise Office","address":"Carmen, Surigao del Sur","sealPath":null},
    "administrator":{"fullName":"Maria Santos","username":"carmen.head","email":"head@carmen.gov.ph"},
    "facilities":[{"code":"NPM","name":"Carmen Public Market","shortName":"CPM","archetype":"DailyStall"}],
    "rates":[{"facilityCode":"NPM","key":"NpmDailyStall","amount":25.00}]
  }'
```

> Verify the exact login response field name for the access token against `POST /api/adminauth/login` in your
> environment before wiring (`accessToken` per the app's `TokenResponseDto`).
