# Restricted AWS deployment runner: StallTrack V10

**Status: setup handoff, not enabled.** No additional AWS instance, GitHub self-hosted runner, production credential, firewall rule, Docker image switch, database change, or operator account has been created by this document.

## Why a separate runner

- Source: public `Clinttttt/EEMO-Cantilan-SDS`, default branch `master`.
- GitHub CI already tests pushes to `master`. Merged PR #40 adds a post-CI GHCR image publisher for API and Client (both tagged with the exact Git commit SHA).
- AWS production: Lightsail `stalltrack-demo-vm-20261017`, Singapore. The running API, Head Portal and PostgreSQL 18 share a Docker Compose deployment. Presentation database: `stalltrack_v10_presentation_clean`.
- The host maintains `stalltrack-db-backup.timer` and `stalltrack-db-restore-drill.timer`, but **timer enabled status alone does not prove backup recoverability**.
- Never register a persistent self-hosted Actions runner in the **public** application repo or grant a CI runner the production Docker socket.

## Isolation and minimum privileges

1. Create a **separate private deployment-control repository** (suggested name `Clinttttt/stalltrack-deploy-private`) with only trusted maintainers, protected default branch, and required reviews for workflow changes. Do **not** copy production tokens into the public repository.
2. Obtain **explicit approval for a new billable AWS Lightsail VM** (including instance class, static IP, storage, monthly estimate). Place it separately from the production database; do not repurpose the production VM as the runner.
3. Install the official GitHub Actions runner on the new VM as an unprivileged `stalltrack-runner` system user, registered **only** to the private repo with labels `self-hosted,linux,x64,stalltrack-deploy-control`. No sudo, `docker` group, production DB credentials, or root login. Verify the official runner package checksum/signature before installation.
4. Outbound HTTPS to GitHub/GHCR is allowed. Permit inbound production SSH only from the runner VM's **static /32 address**, alongside a separate operator recovery rule; never open port 22 to all IPv4 addresses.
5. Production must have a separate `deploybot` SSH identity that cannot access interactive shells, Docker directly or database credentials. A single `authorized_keys` key is restricted with `from=<RUNNER_IP>/32,restrict,command=<ROOT_OWNED_GATE>`; disable port forwarding and PTY.
6. The administrator reviews and owns the gate and its small root-owned deployment helper. The helper accepts **only a fixed, validated input format** (full source SHA and immutable Client manifest digest). It cannot accept arbitrary commands, compose files, paths, image tags or environment variables from the runner. It has an audited narrow sudo rule if absolutely required, with no arbitrary arguments.
7. Production helper independently validates release origin/CI status, exact SHA, digest, last good image, backup evidence, no pending API migrations, live API/DB container IDs and Docker Compose config; it then switches **Client only** with no dependencies, checks live HTTP readiness, and rolls back on any failure. Record the old digest and the result in a root-owned log.
8. **No automatic API cutover initially.** API schema changes and financial workflow changes require an explicitly reviewed compatibility/migration plan, backup, restore drill and separate approval. Never auto-run EF migrations, database reset, restore, Azure import, or operator creation.
9. Secrets live only in the private repo / runner environment or production secret storage. Pin the production SSH host key based on an independently verified fingerprint; do not use `StrictHostKeyChecking=no` or fetch a new fingerprint blindly.
10. Keep the GitHub production environment protected during rehearsal. Only enable scheduled promotion after testing each fail-closed path and rollback. Promote only the latest `master` SHA whose **push** CI succeeded and for which immutable GHCR Client artifacts exist.

## Handoff sequence

**A. Source release:** Confirm post-CI image publisher has a successful run for a known `master` SHA, and both GHCR images exist, are inspectable, and are pinned by immutable digests. A successful CI check is *not* proof that images were published.

**B. Private control:** Create private repository. Add the `infra/aws/templates/private-promotion.yml` template in its `.github/workflows/` directory *only after* reviewing it and configuring a protected `aws-production` environment. Keep manual approvals until test release and rollback are completed.

**C. Isolated VM:** With cost approval, create runner VM. Install verified GitHub runner as `stalltrack-runner` and register **to private repo only**. Confirm it has no Docker/socket/sudo/root privileges and can run only intended workflow jobs.

**D. Production ingress:** Authorized administrator provisions `deploybot` forced-command entry and root-owned, fixed deployment helper. Restrict SSH source by IP. Verify attempts to obtain an interactive shell, forward ports, execute arbitrary commands or deploy unknown SHA/digest are denied.

**E. Backup and correctness gates:** Validate an actual recent pg_dump archive, checksum, independent off-server copy, and isolated PostgreSQL 18 restore drill; preserve old release image. Production helper compares pre/post API & DB container identities and validates Client and API URLs; on failure restore original Client image and Compose configuration, no data changes.

**F. Rollout:** Start with manual dispatch in private repo. Verify nonproduction dry run; prove invalid SHA, registry failure, missing backup evidence, stale CI, SSH host-key mismatch, failed HTTP checks and reversal all fail safely. Only then enable scheduled release promotion after every successful CI on `master` (for example, 10-minute polling in the private repository), retaining a production freeze switch.

## Gates / decisions still needed

- Approval of a **separate billable runner VM** and its estimated monthly cost.
- Creation/confirmation of a **PRIVATE** GitHub deployment repository, its maintainers and environment protections.
- Verified GHCR images and read-only pull rights; no runner registration or SSH secrets available here yet.
- Independent verified AWS host access, backup/restore evidence and approved production forced-command helper.

**Current status:** GitHub-side design only. No production or database mutation performed.
