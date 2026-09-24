---
name: stalltrack-interface-review
description: Review or implement StallTrack Web/Mobile interface changes while preserving canonical workspace ownership, terminology, authorization, financial source authority, tenant-derived labels, and the shared design system.
---

# StallTrack interface review

Use this skill for page, navigation, route, terminology, layout, filter, table, state, or shared-component work.

## Read first

Read:

- AGENTS.md
- docs/README.md
- docs/interface/INFORMATION_ARCHITECTURE.md
- docs/interface/DESIGN_SYSTEM.md
- docs/interface/MIGRATION_PLAN.md
- docs/decisions/DECISION_REGISTRY.md
- relevant business/security documents for the page being changed

## Workflow

1. Identify the workspace owner and whether the change is page-local or shared-shell.
2. State current route(s), canonical route(s), compatibility requirement, and authorization.
3. Trace every displayed financial/status value to its existing source. Never invent presentation arithmetic.
4. Verify tenant-derived names, rates, facilities, sections, and branding remain source-backed.
5. Preserve domain vocabulary: Facility, Occupancy/Term, Payor, Account, Collection, Remittance, Delinquency, Arrears, and urgency are not interchangeable.
6. Apply docs/interface/DESIGN_SYSTEM.md rather than inventing a per-page aesthetic.
7. Cover loading, empty, failure, retry, responsive, focus, and accessibility behavior where the page needs them.
8. Add focused bUnit tests for routes, auth, rendered terminology, filters/actions, and states.
9. Audit the final diff for shared-shell leakage and unrelated domain changes.

## Stop conditions

Stop and hand off when the change requires:

- shared navigation owned by another session;
- a new financial/business rule;
- authorization expansion;
- a new stable account/occupancy identity;
- a future capability that is still hidden;
- backend/source changes outside the approved slice.

## Output

Report route/auth preservation, source authority, terminology, design-system compliance, test evidence, and any unresolved dependency.
