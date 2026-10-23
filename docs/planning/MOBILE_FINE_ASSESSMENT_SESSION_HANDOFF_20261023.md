# Mobile assessed fines in New Collection — 2026-10-23

## Scope and implementation

Branch: `feat/mobile-fine-assessments-v13-20261023` from released `fcf01c44`. Isolated worktree: `C:\dev\stalltrack\eemo-mobile-fines-v13-20261023`. No merge, production deployment, migration, or published collector APK was authorized or performed in this task.

An **existing, authorized, Head-created FineAssessment** with a verified tenant-owned FacilityContract or SpaceAccount can be offered as an item under Collector Mobile's current New Collection UI. The charge is displayed as **Fines / Penalties**, official receipt instrument, with the server-computed **full remaining balance**. The collector cannot type an arbitrary fine amount or create a new assessment. Each choice carries the exact FineAssessment ID and concurrency version, never the occupant's name as authority.

Mobile `CollectionSessionItemKind.FineAssessment = 13` and optional `SessionFineAssessmentIntent` extend the existing session contract without modifying historical item kinds or fingerprint behavior. `CollectionSessionSources.Fines.cs` reuses existing `CollectionComposerWorkflow.QuoteFineAsync` (collector/tenant/account authorization) and `FineAssessmentCollectionSource` (frozen authority, effective OR policy, correction-adjusted balance). Discovery excludes settled or ineligible assessments; quote enforces exact source identity, fine ID, version and full balance. Post repeats all checks and delegates to `CanonicalCollectionPostingCoordinator` with the original typed fine allocation, source snapshot, classification and verified actor/collector. The existing serializable Mobile session transaction encloses the fine claim, Collection/SRC, allocation and other selected items; unchanged `ClientCollectionSessionId` and per-item operation identity enable durable offline retries.

One New Collection checkout can include rent and fine together; it records **one canonical SRC per item**, not a new combined physical OR or a second receivable. Classification stays independent: rent and Fines/Penalties. Compatible OR instrument requirements still apply to each posting; no CT is labeled OR, and no physical accountable-document identifier is fabricated.

## Explicit financial limits

- Existing liability and full unpaid amount only. Partial assessed-fine payment, direct/walk-in fine creation, fine waiver or amount adjustment, and automatic fines for delinquency remain unsupported.
- Scope is only the four existing FineAccountKind categories: FacilityContract, Kanmanggay, VegetableFruitMonthly, FiestaAraw; no new Fish/Meat-only fine identity or generic Business Payor bridge.
- Existing account-linked source discovery is reused. Historical/closed accounts that cannot be found by Mobile's source search will not automatically appear solely because a fine remains owing; expanding historical account selection needs separate eligibility and UX approval.
- Offline queue can retain an already-reviewed fine intent. On connectivity return it **re-quotes and rechecks** the fine's current authoritative outstanding state; if paid/repriced elsewhere it returns Needs Review instead of double-posting. Newly browsing an uncached source entirely offline is governed by the separate Mobile offline prefetch task and is not solved here.
- No new EF migration. Future rollout must deploy new backend support before the corresponding signed Android APK, bump collector app version appropriately and verify synchronized version advertisement.
- Official Monthly Income, Collection Activity, correction, Accountable Forms, remittance and Collector Report use existing classified SRC postings; production tenant financial reconciliation has not been independently exercised and must be part of release acceptance.

## Verification evidence

- Infrastructure Release compile: **passed** (0 errors, existing warnings).
- Focused PostgreSQL 18 Testcontainers tests: **4 passed, 0 failed, 0 skipped**. Tested full fine canonical SRC+allocation, replay/idempotency, source/collector mismatch, partial amount rejection, stale offline quote after Web post, and mixed Kanmanggay rent + fine independently classified SRCs.
- Mobile bUnit fine-picker test: **1 passed**, preserved existing style, no editable amount, stable typed fine item.
- Expanded PostgreSQL FineAssessment integration regression suite: **74 passed / 0 failed / 0 skipped**, including all four new Mobile tests (the new settled-history discovery optimization will be recompiled and rechecked in the final focused pass).
- Entire Mobile CollectionSessionEditor bUnit regression: **67 passed / 0 failed / 0 skipped**.
- Existing session-identity unit regression: **4 passed / 0 failed / 0 skipped**.
- Android MAUI Release `net10.0-android` **build passed / 0 errors / 55 warnings** (compiled and packaged locally, not an authorized signed/published APK). NuGet reported existing AndroidX version constraint warnings, plus a **high-severity advisory for transitive `Microsoft.AspNetCore.SignalR.Common` 9.0.4** (NU1903); dependency remediation is a separate release-hardening task. Test project additionally reported moderate-severity AngleSharp 1.2.0 (NU1902).
- Not yet authenticated visual acceptance on a real mobile phone or production read/write acceptance. No real fine was created or settled against production data.

## Files

- `EEMOCantilanSDS.Application/Dtos/Mobile/CollectionSessionDtos.cs`
- `EEMOCantilanSDS.Application/Common/Revenue/CollectionSessionWorkflow.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.Native.cs`
- `EEMOCantilanSDS.Infrastructure/Repositories/Revenue/CollectionSessionSources.Fines.cs` (new)
- `EEMOCantilanSDS.Mobile/Components/Shared/CollectionSessionEditor.razor`
- `EEMOCantilanSDS.IntegrationTests/FineAssessmentTests.Mobile.cs` (new)
- `EEMOCantilanSDS.IntegrationTests/FineAssessmentTests.MobileMixed.cs` (new)
- `EEMOCantilanSDS.Mobile.ComponentTests/CollectionSessionEditorTests.cs`

External release-progress log: `C:\dev\stalltrack\release-evidence\V13_MOBILE_ASSESSED_FINES_IMPLEMENTATION_PROGRESS_20261023.md`.
