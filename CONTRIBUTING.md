# Contributing to StallTrack

StallTrack is a production multi-tenant LGU revenue system. Changes should be bounded, reviewable, and evidence-driven.

## Before changing code

1. Read AGENTS.md.
2. Read docs/README.md and the relevant canonical documents.
3. Inspect the current implementation and nearest existing pattern.
4. State the exact behavior being changed and what must remain unchanged.
5. Identify tenant, authorization, financial, reporting, and migration risk before coding.

## Branch and worktree discipline

Use one owning worktree/session per bounded area. Do not edit another session's working-tree files.

Normal Git operations from an assigned worktree may use the repository's shared .git/worktrees metadata.

Start implementation slices from the required current origin/master baseline after their dependencies are satisfied.

Keep one PR focused on one bounded slice.

## Implementation rules

- Preserve Clean Architecture dependency direction.
- Keep specialized financial domains authoritative.
- Do not calculate money in presentation code.
- Resolve tenant-owned data through established tenant boundaries.
- Preserve route compatibility during Interface V2 migration unless retirement is explicitly approved.
- Preserve current authorization unless a separate security decision changes it.
- Keep future capability hidden until it is functional and authoritative.
- Use scoped component CSS and shared design tokens; follow docs/interface/DESIGN_SYSTEM.md.

## Verification

Run focused tests first, then the relevant full suite.

Use docs/testing/TESTING_STRATEGY.md to choose the right test layer.

Always run git diff --check before committing.

Review the complete diff for scope leakage.

## Documentation

When behavior, architecture, security policy, interface structure, or an accepted decision changes, update the canonical document in the same bounded change.

Do not duplicate one rule across several documents when a link to the canonical source is sufficient.

Historical planning/evidence is not current authority unless explicitly promoted.

## Production

A push to master can deploy production. Follow docs/operations/PRODUCTION_VERIFICATION.md after release.

Never stage or commit .env files, keystores, database dumps, generated artifacts, or secret material.
