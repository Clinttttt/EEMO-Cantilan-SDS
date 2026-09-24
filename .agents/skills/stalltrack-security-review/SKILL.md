---
name: stalltrack-security-review
description: Review StallTrack authentication, authorization, tenancy, tokens, MFA, secrets, cross-tenant queries, sensitive mutations, or security-sensitive route changes.
---

# StallTrack security review

Use this skill whenever a change can alter who may access data/actions, how identity is established, how tenant isolation works, or how sensitive credentials/tokens are handled.

## Read first

Read:

- AGENTS.md
- docs/README.md
- docs/security/SECURITY_ARCHITECTURE.md
- docs/security/TENANT_ISOLATION.md
- docs/architecture/ARCHITECTURE_RULES.md
- relevant decision/business documents

## Workflow

1. Define the trust boundary: caller, role, tenant, credential/token, endpoint/action, and protected resource.
2. Trace authorization at both presentation and API/action layers.
3. Trace tenant resolution and global filters. Treat every IgnoreQueryFilters() as security-sensitive.
4. Verify user/collector/tenant identity comes from authenticated context where it should, not request-body claims.
5. Check token transport, lifetime, hashing/revocation, MFA, lockout, and secret-storage compatibility when relevant.
6. Verify CORS/rate-limit/security-header implications for pipeline changes.
7. Check logs/audit output for secret or cross-tenant exposure.
8. Add focused tests; use PostgreSQL/Testcontainers when query filters, constraints, or database behavior provide the guarantee.
9. Confirm no existing role or municipality gains unintended access.

## Stop conditions

Stop if the requested change would:

- weaken tenant fail-closed behavior;
- broaden Platform Operator/Head semantics without an explicit decision;
- expose raw secrets/tokens;
- replace compatible password/token behavior without a migration;
- use navigation visibility as authorization;
- bypass tenant filters only for convenience.

## Output

Report threat/boundary, authorization and tenancy paths, sensitive data handling, tests, and any unresolved security decision.
