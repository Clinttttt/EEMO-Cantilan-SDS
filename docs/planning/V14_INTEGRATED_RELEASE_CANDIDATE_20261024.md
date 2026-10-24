# StallTrack V14 — Integrated release candidate (24 October 2026)

## Source and scope
- Release candidate worktree: `C:\dev\stalltrack\v14-integrated-release-candidate-20261024`; branch `integration/v14-combined-20261024`.
- Base: `origin/master` at `f5acf1bbf94b031949fa9ba4a6d26de3517adc40`, last verified production API/Head and signed Collector 1.1.18/code20.
- Includes three prior isolated mobile commits: `3fff70a3` Phase 1 offline preloading, `fd260fc3` unpaid legacy monthly-payment collector ownership safeguard, and `b80381b1` unified newest-first collector Records and confirmation messaging.
- Includes the 17 exact byte-for-byte source/test/docs files from Codex's **uncommitted** `fix/v14-branding-weighing-20261024` worktree (commit base `f5acf1bb`); `docs/planning/CURRENT_RELEASE_STATE.md` was deliberately not wholesale copied to avoid overwriting release facts. The Codex branch/worktree remains dirty and unchanged.
- Integrates authenticated-tenant branding (controller authorization correction, tenant-id lookup, retry and profile-driven sidebar refresh), in-place Weight & Measure registered Fish/Meat and distinct historical NPM evidence, and the scoped read-only CSV/report updates.
- Adds optional collection `RecordedAtUtc` API DTO property for chronological Mobile Records; no schema migration. Existing public official-report business-date ordering retained. On older API versions the app shows date-only for canonical entries and uses a business-date sort fallback, not fabricated recording times.
- Existing authenticated collection/source ownership, SRC correction policy, offline queue and sync idempotency are preserved. No full off-network quotation/source capture was authorized or added.

## Source audit
- Exact SHA-256 comparisons of the 17 copied Codex files in the new worktree: 17/17 match originals; changes to `CURRENT_RELEASE_STATE.md` manually appended as a current candidate notice.
- Codex's previous isolated results: 32 unit; 129 Head; 27 PostgreSQL; 168 Mobile; three Monitoring tests; API, Client and Windows Mobile Release builds. Those are not proof of combined behavior. Combination test logs are `.v14-integrated-*.log` in this worktree; record actual results before shipping.
- Mobile branch prior isolated results: 3,138 unit, 168 Mobile, Android-target compile 0 errors; full mobile and Android acceptance still separate. Candidate regression results supersede these where available.

## Mandatory production gates
1. **Finance:** The Iceplant refusal has a plausible code-level cause (stale CollectorId on an unpaid PaymentRecord), but source-specific authoritative production evidence has NOT established the exact record's ledger/SRC/OR/settlement/queue status. The handler fix only permits an Unpaid/zero-payment/no PaidAt/no OR record through ownership guard, while preserving another collector's actual receipts. Confirm recorded authority, and review PostgreSQL concurrency tests before deploying. Never rewrite an existing payment, synthesize SRCs or clear queues.
2. **Tenant policy:** Fish/Meat requests are configured disabled on production despite the migration/table existing. Do **not** enable without municipality/operator allow-list and effective deployment/schema read. Canonical slaughter approved charges are enabled only where the authoritative LGU policy and terms say so.
3. **Release order:** API/Client production promotion must use successful master CI, immutable image digests and private AWS deployment gate with backup/release record. Only then publish a signed Android Collector with a **new monotonic version code** (greater than code20), matching source SHA. Never replace the presentation-ready production APK with a mismatched test signing certificate.
4. **Phone:** The Infinix may retain pending `Waiting to sync` operations inside the separate preview package `site.stalltrack.collector.offlinetest`. Do not uninstall, clear storage, or replay/recollect pending money until its authoritative status is reconciled. Real Android login/cold/offline reconnect, branding, weighing responsive layout and Records screenshot QA remain pending.
5. **Distribution configuration:** Production `/api/mobile/version` was previously advertising code15 while the release asset was code20. Any release requires approved privileged version-advertisement update; do not lower min-supported or bypass runtime approval.
6. No production migration, tenant activation, deployment, financial write, policy change or APK install is implied by preparing this candidate.

## Combined execution results
See appended check outputs and final commits below. Until they are recorded, no 'production ready' claim.

## Verified integrated local run — 2026-10-24
All suites executed against this combined source tree, .NET 9/10 Release; no mocked claim of a successful app install.

| Gate | Exact result |
| --- | --- |
| All unit tests | 3,139 passed; 0 failed/skipped |
| All Head component tests | 1,440 passed; 0 failed/skipped |
| All Mobile component tests | 168 passed; 0 failed/skipped |
| Scoped disposable PostgreSQL integration | 27 passed; 0 failed/skipped |
| API Release | Exit 0; 0 errors, 18 warnings |
| Client Release | Exit 0; 0 errors, 95 warnings |
| Android net10.0-android Release compile-only | Exit 0; 0 errors, 32 warnings |
| Source whitespace / Git stage | Clean |

The scoped PostgreSQL filter includes real authenticated branding endpoint tests, default-off registration-policy lifecycle, independent Fish/Meat weighing, frozen versions and remittance grouping. This is not the entire PostgreSQL suite: GitHub PR CI must run the repository's full Docker/PostgreSQL gate.

The package warnings include known pre-existing Xamarin.AndroidX/NuGet version-constraint diagnostics; no new runtime behavior was asserted from compiler exit alone. No physical Android installation, full signed release, rendered Head/Report screenshot comparison or live financial mutation occurred.

GitHub Draft PR #53: https://github.com/Clinttttt/EEMO-Cantilan-SDS/pull/53. Branch SHA originally `1eb7330a55f9ce3a0b80bbf809fd0d41d7bab21d` before this evidence-only documentation commit. Production is still `f5acf1bb` and official Collector 1.1.18/code20. This candidate is ON HOLD for signed/mobile promotion pending the explicitly documented financial record and tenant policy gates; merging to master would trigger CI/image publishing and must not be mistaken for deployment approval.
