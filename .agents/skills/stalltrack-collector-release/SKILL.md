---
name: stalltrack-collector-release
description: Prepare, publish, or verify a StallTrack Collector Android release, including MAUI release validation, display/version-code coordination, signed APK publication, GitHub Release delivery, and API update advertisement. Use for collector release or versioning work, not routine mobile coding that does not need a release.
---

# StallTrack Collector release

Publish one identifiable signed APK and ensure collectors are advertised that same build.

## Authority and authorization

Read the root `AGENTS.md`, `EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj`, and
`.github/workflows/publish-apk.yml` before acting. Treat the workflow as the release mechanism. Explicit user instructions
govern the requested release, but surface any conflict with the monotonic version or signed-build requirements.

Do not dispatch the workflow, create or replace a GitHub Release, change Azure app settings, or expose signing material
unless the current request authorizes the corresponding action. Never inspect or print secret values.

## Release workflow

1. Determine whether the requested change requires collectors to install a new APK. API-only, portal-only and guidance-only
   changes do not. A mobile behavioural or packaged-asset change does.
2. Establish the version already shipped and advertised. Compare the project `ApplicationDisplayVersion` and integer
   `ApplicationVersion` with the latest Collector GitHub Release and the API mobile-version response.
3. Choose one display version and one integer version code for the new build. The code must be greater than the currently
   advertised and shipped code; never advertise backward. Update both project values when the release should remain
   reproducible from source, or pass both workflow inputs when the user explicitly wants a release override.
4. Before publication, run the relevant unit/component tests and a Release Android build when the local environment has
   the required MAUI workload and Firebase configuration. Report unavailable signing or platform prerequisites rather
   than substituting a debug or unsigned build.
5. Use the manual `Publish Collector APK` workflow for the signed release. It must resolve the actual built versions,
   publish the non-empty asset as `stalltrack-collector-latest.apk`, mark the matching `collector-{name}-{code}` release
   latest, and advertise those exact values to the API when Azure OIDC is available.
6. Verify the release asset is downloadable and non-empty, its release title/tag matches the built version, and
   `/api/mobile/version` reports the same version name and code after the API restart window. If advertisement was skipped
   or did not converge, report that collectors are not yet reliably prompted; do not claim the release complete.
7. Preserve mobile collection invariants while reviewing release changes: offline writes remain idempotent through
   `ClientOperationId`, queued money is not discarded by a transient failure, and collection screens prefer the
   server-issued business date with device-local fallback.

## Result

Report source SHA, display version, version code, test/build evidence, workflow and release URLs, asset verification, API
advertisement, and any manual follow-up. Do not treat a GitHub tag alone as proof of the APK's embedded version.
