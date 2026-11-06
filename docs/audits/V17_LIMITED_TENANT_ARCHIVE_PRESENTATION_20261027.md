# V17 Head Portal daily/manual limited archives and historical payment labeling — 2026-10-27

## Scope

The production Head Portal backup scheduler and Create backup now command previously used only the full restore-v2 snapshot path. The physical database retains 13 historical financial tables outside the EF-owned table manifest. This correctly blocked tenant-scoped restore, but also blocked any new stored backup, even for a strictly read-only archive. The existing whole-database AWS PostgreSQL backup/restore drills are independent and remain the authoritative disaster-recovery strategy.

## Implementation

- `ITenantRestoreRepository.CreateSnapshotAsync` remains a strict restore-v2 capture that **fails closed** if any unmanaged table exists.
- `ITenantRestoreRepository.CreateStoredBackupSnapshotAsync` captures the model-approved physical tenant tables under PostgreSQL repeatable-read and `SET TRANSACTION READ ONLY`. When unmanaged legacy tables are detected, it emits the distinct `archive-v1` format with an explicit `ExcludedLegacyTables` manifest. It does **not** query or export those physical legacy tables, credentials, or excluded history; this archive is incomplete by design.
- Both `ValidateArchive` and `Validate` enforce tenant code/municipality, exact server-owned columns, value shapes, table manifest, SRC sequencing and schema fingerprint. For `archive-v1`, the excluded physical table set must match the server catalog exactly.
- The existing destructive Restore repository and upload/stored restore handlers still accept **restore-v2 only** and require a fully restorable physical schema. They reject archive-v1 before any financial mutation, and the Head Portal disables Restore on archived stored rows.
- Stored backup metadata exposes `IsRestoreReady` computed from the actual persisted format (no migration). The Head Portal displays `Limited archive · not restorable` and distinguishes archival files in their filenames. Manual and scheduled captures keep separate retention (15 each); the existing scheduled PH-calendar-day idempotence is unchanged.
- The short historical payment evidence note replaces an overly long UI warning. Collection Activity uses `Not issued (historical record)` instead of presenting the label `Legacy payment` as a stand-in for an SRC. Already-paid money, physical OR evidence, SRC allocation and ST-20 settlement authority are never changed.
- No database schema or financial transaction writing is altered, no synthetic SRC is assigned, and no production restore is exercised.

## What the panel demonstration may show

1. As a Head Portal user, select Settings → Backups → Create backup now.
2. Review the new automatic/manual record count and the **Limited archive — not restorable** label. Click View to inspect its approved table manifest, or Download to receive JSON marked NOT-RESTORABLE.
3. The Restore action stays disabled for the limited archive; **do not demonstrate or attempt production restore**.
4. Show the independently protected AWS PostgreSQL daily backup and successful restore-drill evidence as the system-wide recovery guarantee.

The archive does not contain the 13 retained historical tables, so it must never be called a complete municipality backup or used for recovery. These omissions must be addressed separately by a verified full historic-table strategy.

## Verification (append actual outcomes)

- Focused unit backup repository and restore-v2 safety tests: **57 passed, 0 failed**.
- Disposable PostgreSQL 18 (Docker/Testcontainers) archive capture, stored backup read, archive-v1 classification, foreign-tenant exclusion, restore refusal: **1/1 passed**.
- Existing PostgreSQL daily scheduling/current PH day/read-only selection tests: **3/3 passed**.
- Focused Head Portal component checks (includes historical reference panel and backup UI): **45/45 passed**.
- Full independent CI/release build and live browser verification: pending.
- Production readiness and actual authenticated Head Portal capture: pending.
- No production database edits, migrations, changes to AWS system backup, or restore operations in this worktree.
