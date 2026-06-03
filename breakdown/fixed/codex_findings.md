# CODEX / GPT Findings — Breakdown & Status

Validated by Opus against the actual code, then implemented. ~~Strikethrough~~ = done/fixed.

**Legend** — Verdict: CONFIRMED (real) · DESIGN (real, needs a decision) · REFUTED (not real / already fixed).
Status: ✅ Done · 🟡 Partial · ⬜ Open.

---

## Authentication

| ID | Finding | Severity | Verdict | Status |
|----|---------|----------|---------|--------|
| A1 | ~~Blazor refresh flow effectively broken (wrong cookie/claim + wrong route/host)~~ | P1 | CONFIRMED | ✅ Done |
| A2 | ~~API refresh endpoint ignores JSON refresh-token body~~ | P1 | CONFIRMED | ✅ Done |
| A3 | ~~Inactive/locked accounts can still log in and refresh~~ | P1 | CONFIRMED | ✅ Done |
| A4 | ~~Failed-login lockout is dead code~~ | P1 | CONFIRMED | ✅ Done |
| A5 | ~~API authorization too broad (plain `[Authorize]`)~~ | P1 | CONFIRMED | ✅ Done |
| A6 | ~~Refresh tokens plaintext at rest + logout doesn't revoke~~ | P2 | CONFIRMED | ✅ Done |
| A7 | Cookie/JWT token model mixed across 3 stores | P2 | DESIGN | ⬜ Open |

## Timezone / Reports

| ID | Finding | Severity | Verdict | Status |
|----|---------|----------|---------|--------|
| T1 | ~~NPM revenue double-counted in trend/top-stall widgets (also yearly trend)~~ | P1 | CONFIRMED | ✅ Done |
| T2 | ~~PaymentHistoryModal uses server-local time + header counts partials as full unpaid~~ | P1/P2 | CONFIRMED | ✅ Done |
| T3 | ~~Payment-history repository uses UTC month boundaries~~ | P2 | CONFIRMED | ✅ Done |
| T4 | ~~Report year validation uses UTC year~~ | P2 (low) | CONFIRMED | ✅ Done |
| T5 | ~~`PhilippineTime.Now` exposes a dangerous Kind=Utc DateTime~~ | P2 (latent) | DESIGN | ✅ Done (hardened) |
| T6 | ~~Weekly report labels inaccurate / misleading~~ | Low | CONFIRMED | ✅ Done |
| — | `TodayUtcRange` correctness + leap-year/week-5 handling | — | LOOKS CORRECT | ✅ Verified |

## Collector Identity

| ID | Finding | Severity | Verdict | Status |
|----|---------|----------|---------|--------|
| C1 | ~~TPM lets callers forge collector attribution (client-supplied CollectorId)~~ | P1 | CONFIRMED | ✅ Done |
| C2 | ~~TRM/SLH do not record authenticated collector identity~~ | P1 | CONFIRMED | ✅ Done |
| C3 | ~~Admin ids stored in CollectorId (payment/daily)~~ | P2 | CONFIRMED | ✅ Done (actor model) |
| C4 | Authorization does not enforce collector facility assignment | P1 | CONFIRMED | ⬜ Open (needs collector mobile auth) |
| C5 | ~~OR-number update corrupts attribution/timestamps~~ | P2 | REFUTED | ✅ N/A (already fixed; reviewer saw stale code) |
| C6 | ~~Audit logs exist but are never populated~~ | P2 | CONFIRMED | ✅ Done (audit interceptor) |
| C7 | Collector reports omit TPM/TRM/SLH | P2 | CONFIRMED | ⬜ Open (needs collector auth + C1/C2 data) |
| C8 | DDD muddle around collector identity (raw ids + audit strings) | P2 | CONFIRMED | 🟡 Partial (actor model feeds it consistently; domain VO optional) |

---

## Summary

- **Done (15):** A1, A2, A3, A4, A5, A6, T1, T2, T3, T4, T5, T6, C1, C2, C3, C6 — plus C5 (refuted/already fixed).
- **Partial (1):** C8 (consistent attribution in place; full domain Actor value object optional).
- **Open (3):** A7 (single token-source design), C4 (facility-assignment enforcement), C7 (TPM/TRM/SLH in collector reports).
- **Blocker for C4 & C7:** collector **mobile** login (`CollectorAuthController` is empty). The actor model is already in place, so both become small once collectors can authenticate.

## Verification

All implemented fixes build clean (Api, Client, Infrastructure) and pass the test suite (**49/49**), including new regression tests:
LoginCommandHandlerTests, AuthTokenServiceTests, FacilityReportsNpmDedupTests, ActorAttributionTests, AuditInterceptorTests, RecordDailyCollectionCommandHandlerTests.
