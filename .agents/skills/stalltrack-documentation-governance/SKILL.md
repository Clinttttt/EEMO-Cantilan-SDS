---
name: stalltrack-documentation-governance
description: Create, move, reconcile, or review StallTrack canonical documentation without changing business meaning, losing authority precedence, or leaving stale repository references.
---

# StallTrack documentation governance

Use this skill for documentation architecture, ADRs, canonical-rule updates, file moves, and knowledge-base maintenance.

## Authority

docs/README.md defines the canonical map and authority order. AGENTS.md is the concise agent entry point.

## Workflow

1. Classify the material: canonical rule, architecture rationale, business semantics, interface rule, security rule, decision, testing/operations runbook, historical planning, or evidence.
2. Prefer one canonical home and links over duplicated rule text.
3. When moving a file, preserve content first; do not silently rewrite business meaning during relocation.
4. Update all repository references: AGENTS.md, README.md, skills, tests that read docs, code comments, workflow path filters, and internal Markdown links.
5. Keep historical material clearly labeled as historical/evidence.
6. Distinguish current production from target/future capability.
7. Run git grep for old paths/names, validate relative links, and run focused tests that load canonical docs.
8. Use an ADR when one long-lived architecture decision deserves its own status, context, decision, and consequences.

## Do not

- let a procedural skill override canonical docs;
- treat current code as automatic business authority;
- convert a file move into a financial/security/interface redesign;
- duplicate the same rule into many documents;
- leave tool-specific knowledge as the only source of truth.

## Output

Report moved/created documents, authority changes (if any), updated references, validation, and remaining stale/historical references.
