# StallTrack Tenant Isolation

**Status:** Canonical multi-tenancy security rules.

**Scope:** Municipality-owned persistence, tenant resolution, cross-tenant exceptions, uniqueness, cache/query boundaries, and review requirements.

## 1. Core rule

Every municipality is a tenant. Tenant-owned data must be resolved and filtered by MunicipalityId.

Tenant isolation is a security invariant. It must not depend on the user interface remembering to add a filter.

Cantilan is the accuracy baseline, but Cantilan data must not become a global default for an authenticated request that should resolve another municipality.

## 2. Persistence model

Tenant-owned entities implement the municipality-owned contract.

AppDbContext applies global EF Core query filters by model type.

For a tenant-owned, soft-deletable entity the effective read rule is:

- row is not soft-deleted; and
- when a tenant accessor exists, MunicipalityId equals the resolved current municipality.

Tenant-owned non-soft-deletable entities receive municipality isolation without the soft-delete clause.

New tenant-owned entity types must be covered automatically by this mechanism and by tenant-filter coverage tests.
## 3. Fail-closed behavior

The code distinguishes:

1. no tenant accessor exists at all;
2. an accessor exists but has not resolved a municipality;
3. an accessor resolves a municipality.

No accessor is permitted for specific design-time/tooling/test contexts that intentionally operate outside a request tenant.

When an accessor exists but MunicipalityId is unresolved/empty, tenant-owned queries match nothing.

This fail-closed behavior prevents "unknown tenant" from turning into "all tenants."

Do not change that fallback direction.

## 4. Cross-tenant reads

IgnoreQueryFilters() is security-sensitive.

Acceptable categories include only paths whose cross-tenant need is explicit, such as:

- sign-in or token refresh before tenant context can be established;
- platform-operator operations authorized for cross-tenant work;
- backup/restore;
- migrations/seeders/tooling;
- narrowly justified recognition/lookup paths with explicit safeguards.

Every new use must be reviewed for:

- why tenant context cannot be used;
- what other filters must still apply;
- whether multiple tenants may match;
- whether the caller has explicit cross-tenant authority;
- whether the result can leak tenant identity or data.
## 5. Uniqueness

Business uniqueness is generally tenant-scoped unless a specific global identifier is deliberately designed otherwise.

Examples include:

- usernames where policy permits tenant reuse;
- email/contact identity where the business rule is tenant-scoped;
- stall/space numbers;
- OR/accountable-document numbers within the applicable tenant/instrument/series rules;
- facility configuration identity.

Never implement a global uniqueness assumption merely because Cantilan currently has no duplicate.

A cross-tenant lookup must expect multiple logical matches unless the identifier is explicitly defined as globally unique.

## 6. Authentication seam

Access tokens carry municipality identity used by authenticated request tenancy.

Refresh is different: the access token may be expired, so the refresh-token lookup cannot depend on the current JWT tenant claim.

The current refresh flow intentionally bypasses tenant query filters to find the hashed refresh token across users, then applies active/soft-delete/refresh validity rules.

That exception is not a pattern for normal repositories.

## 7. Platform Operator

Platform Operator is a distinct cross-tenant authority.

A municipality Head/SuperAdmin is not a Platform Operator.

Whole-platform operations must use the dedicated policy/guard rather than assuming SuperAdmin implies global access.

Do not widen a municipality role to make an operator workflow convenient.
## 8. Tenant-derived presentation

Anything visible to a municipality user should be tenant-resolved where the platform supports it.

Examples:

- municipality/office name and acronym;
- facility names and short codes;
- section names;
- fee rates;
- market day;
- branding;
- provider/channel configuration;
- accountable-form policy when implemented.

Do not hardcode "Cantilan", "EEMO", facility names, or Cantilan fee amounts into reusable operational UI.

Reference-tenant evidence may appear in tests/docs where it is explicitly identified as Cantilan evidence.

## 9. Rates and effective dates

Tenant isolation includes effective configuration, not just row ownership.

Rates must resolve through IFeeRateResolver as of the relevant date.

A tenant-specific override must not change another municipality's figure.

Fallback ordinance constants are compatibility defaults, not a reason to bypass tenant configuration.

## 10. Caching

Any cache containing tenant-owned data must include tenant identity in its key/namespace or otherwise be scoped so one municipality cannot receive another municipality's value.

When reviewing a cache:

- identify the tenant discriminator;
- identify invalidation on tenant-owned mutations;
- verify fallback behavior when tenant resolution fails;
- verify no static/shared mutable state carries one tenant's facility/profile/config into another.
## 11. Write safety

Tenant-owned writes must be stamped/validated against the resolved tenant through the established persistence/interceptor boundary.

Do not accept MunicipalityId from ordinary user input as authority for where a row belongs.

For commands involving referenced entities, verify referenced rows belong to the same tenant.

Cross-tenant identifiers in a request must fail rather than silently attaching data to the current municipality.

## 12. Testing requirements

A tenancy-sensitive change should include the narrowest tests proving:

- tenant A sees tenant A rows;
- tenant A cannot see tenant B rows;
- an unresolved tenant with an accessor sees no tenant-owned rows;
- deliberate no-accessor tooling/test behavior remains intentional;
- uniqueness is enforced at the correct scope;
- any IgnoreQueryFilters() path has the required extra guards;
- cache keys/invalidation do not cross tenant boundaries.

Use PostgreSQL/Testcontainers integration tests when correctness depends on EF translation, database constraints, query filters, or indexes.

## 13. Review checklist

Before approving a tenant-sensitive change, answer:

- What entity/data is tenant-owned?
- Where is MunicipalityId resolved?
- Does the global filter cover it?
- Can the request choose or spoof tenant identity?
- Is any query bypassing filters?
- Could a cross-tenant lookup return multiple matches?
- Is uniqueness tenant-scoped?
- Are cache keys tenant-specific?
- Are tenant-derived labels/rates used in UI?
- Does the change preserve Cantilan without imposing Cantilan on other LGUs?
- Is cross-tenant authority limited to Platform Operator or another explicitly approved pre-tenant path?

Any unanswered item is a review blocker for tenant-sensitive work.
