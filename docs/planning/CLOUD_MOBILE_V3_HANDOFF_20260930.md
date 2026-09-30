# Claude Cloud Mobile V3 handoff — 2026-09-30

**Repository:** `Clinttttt/EEMO-Cantilan-SDS`
**Branch:** `interface-v3/mobile-v3`
**Production baseline commit:** `5489c527f7de43e107d342d1a7dbfb392b0a964a`

Before work, verify the repository, branch, and HEAD. This branch starts at the accepted V3 production baseline. Use repository-relative paths; do not expect local Windows worktree paths.

## Accepted backend and UI baseline

- Latest V3 production Web/API release, including Operations composition, truthful unsupported-operation workspaces, and Accountable Forms UI.
- Server operation-capability endpoint and collector operation assignments.
- WCF backend collection authority and safeguards.
- Current Mobile offline architecture: authenticated session restore, tenant binding, facility assignment, offline read cache, durable write queue, `ClientOperationId`, queue ownership, retry/sync, reconciliation-required states, physical Cash Ticket retention, FCM, version/update handling, and specialized facility workflows.

Preserve those Mobile behaviors while designing and integrating Mobile V3.

## Known Mobile gaps to verify

- WCF source remains NPM-bound; canonical WCF Records/Reports coverage needs verification and may need backend support.
- TRM target Cash Ticket writer is absent.
- Slaughter custom-rate governance needs review.
- Market Fees writer is absent.
- Vegetable/Fruit writer is absent.
- Landing/Berthing writer is absent.
- Transfer Large Cattle writer is absent.
- Fish/Meat Vendor Fee source is absent.
- Weight & Measure production writer and cutover are absent.

Treat these as findings to verify. Do not invent business or financial policy to fill the gaps.

## Cloud boundaries

- No production deployment.
- No master merge.
- No APK publication.
- No backend financial invention.
- No production migration.
- No source cutover.

This session owns Collector Mobile V3 design and functional client integration only. It must not redo the Web release.
