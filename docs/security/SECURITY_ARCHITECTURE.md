# StallTrack Security Architecture

**Status:** Canonical security guidance for StallTrack.

**Scope:** Web/API/Mobile authentication, authorization, tenancy, credentials, security headers, rate limiting, audit boundaries, and security-change review.

This document records current verified implementation boundaries. It does not authorize security redesign by itself.

## 1. Security principles

1. Tenant-owned data fails closed when a tenant should exist but cannot be resolved.
2. Authorization is enforced at the route/API/action boundary, not inferred from navigation placement.
3. Secrets and tokens are never stored in source.
4. Stored credentials and long-lived tokens are hashed or protected at rest according to their purpose.
5. Financial and accountable mutations preserve actor/audit evidence through the established architecture.
6. Cross-tenant access is exceptional, explicit, and reviewable.
7. Authentication changes must preserve compatibility with existing credentials unless a migration is explicitly designed.
8. Mobile writes remain retry-safe and identity is derived from authentication rather than request-body claims.

## 2. Authentication model

The API uses ASP.NET Core JWT bearer authentication with custom StallTrack user/auth flows rather than the full ASP.NET Identity application stack.

JWT validation requires issuer, audience, lifetime, and signing key. JWTs are signed with the configured symmetric key using HMAC-SHA512.

The API normally reads the access token from the accessToken cookie. SignalR hub connections may provide access_token in the query string on /hubs paths.

Do not add alternative token sources casually.
## 3. Browser token transport

Successful Web/Payor authentication writes:

- accessToken: HttpOnly, Secure, SameSite=Strict, expires after 15 minutes;
- refreshToken: HttpOnly, Secure, SameSite=Strict, expires after 7 days.

The browser does not need JavaScript-readable bearer tokens for normal portal authentication.

Current durations are also represented by DomainRules.AccessTokenMinutes and DomainRules.RefreshTokenDays. Security changes must keep cookie and token lifetimes aligned.

Logout clears auth cookies and revokes persisted refresh-token state through the existing flow.

## 4. Refresh-token lifecycle

Refresh tokens are generated from cryptographically random bytes.

The raw refresh token is returned to the client/cookie. The persisted value is a SHA-256 hash.

A valid refresh requires the matching user/token state to remain active and refreshable under the user-domain rule.

Refresh lookup intentionally uses IgnoreQueryFilters() because refresh happens without a valid access-token tenant claim. This exception is justified by the refresh token itself being the globally unique secret used to locate the account.

Do not generalize this exception into ordinary cross-tenant reads.

Credential changes and explicit logout revoke or clear refresh-token state according to existing domain behavior.
## 5. Password storage and lockout

Password hashing is behind IPasswordHasher and implemented by ASP.NET Identity PasswordHasher<BaseUser> using its compatible default format.

Existing hashes carry their own format/salt/iteration information. Do not casually change hasher options or algorithm configuration; doing so can invalidate existing account access.

Malformed stored hashes are treated as failed password checks rather than authentication exceptions.

One shared lockout rule applies across user types:

- 5 failed attempts;
- 15-minute lockout.

Successful credential-change/reset paths clear relevant lockout/refresh state according to the domain methods.

## 6. Multi-factor authentication

StallTrack supports TOTP-based MFA, recovery codes, enrollment, verification, disable/regeneration, and administrative reset through the existing application/API flows.

Current product policy:

- MFA is available to supported accounts and is currently optional;
- a Head / SuperAdmin without MFA receives a one-time, dismissible reminder recorded server-side;
- once MFA is enabled, the password step yields only the short-lived MFA challenge and no session tokens until verification.

MFA secrets are sensitive credentials and must remain behind the existing credential-protection boundary.

Do not consume a one-time token in Blazor OnInitializedAsync; prerendering can execute initialization twice.
## 7. Authorization model

Primary application roles include:

- SuperAdmin / Head;
- Admin;
- Collector;
- Payor.

Platform Operator is a distinct cross-tenant authority and is not synonymous with a municipality Head.

The API defines a dedicated PlatformOperator policy using the dedicated platform-operator claim/guard semantics.

Rules:

- changing a page's workspace does not change its authorization;
- navigation visibility does not replace route/API authorization;
- Collector access to an underlying action does not automatically grant Collector access to a Head/Admin Web page;
- Payor access remains own-account scoped;
- destructive or sensitive actions may have stricter authorization than the containing page.

Preserve existing guards unless an explicit security decision changes them.

## 8. Tenant isolation

Tenant isolation is a security boundary, not a UI filter.

Tenant-owned entities implement the municipality-owned contract and are covered by EF Core global query filters.

With a tenant accessor present:

- a resolved municipality sees only its own tenant-owned rows;
- an unresolved municipality matches no tenant-owned rows.

A context constructed without a tenant accessor is reserved for design-time/tooling/test paths that legitimately operate without request tenancy.
IgnoreQueryFilters() is a deliberate security-sensitive operation. Each use must have a documented reason, such as:

- pre-tenant authentication/refresh;
- seed/migration/tooling work;
- backup/restore;
- explicitly authorized Platform Operator work.

See TENANT_ISOLATION.md for detailed rules.

## 9. CORS and request throttling

CORS uses configured origin allowlists:

- development origins from development configuration;
- production origins from production configuration.

Credentials are allowed only for those configured origins.

Auth endpoints use a fixed-window rate limit partitioned by effective client IP:

- 30 requests per minute;
- no queue;
- HTTP 429 on rejection.

The implementation accounts for Azure App Service forwarding through X-Forwarded-For.

Rate limiting supplements account lockout; it does not replace it.

## 10. Response security headers

The non-development API pipeline applies baseline headers including nosniff, frame denial, no-referrer, restrictive permissions, HSTS, and an API CSP of default-src 'none'; frame-ancestors 'none'.

Development Swagger is exempt from this production-only middleware so its UI can function.
## 11. Secrets and protected configuration

Never commit:

- JWT signing secrets;
- database connection strings;
- encryption keys;
- PayMongo credentials;
- Firebase credentials;
- TOTP secrets;
- private keys/keystores;
- real production environment files.

Runtime secrets come from environment/platform configuration.

Sensitive tenant payment credentials use the established credential-protector boundary; do not expose stored secret material to UI DTOs or logs.

## 12. Audit and mutation accountability

Audit evidence and domain history are separate concerns.

The existing auditing infrastructure records mutation evidence for supported audited entities/actions. A generic AuditLog does not automatically equal a complete business lifecycle history.

For example, current OR-field correction audit evidence must not be described as the future AccountableDocument void/replacement lifecycle.

Security-sensitive mutations should preserve actor identity from authentication, tenant identity, timestamp, and before/after or action evidence where the existing audit architecture supports it.

Do not trust user-supplied actor/collector identifiers when the authenticated identity already determines them.
## 13. Mobile security

Collector Mobile is a separate presentation surface with its own authenticated flows.

Important invariants:

- Collector identity comes from authentication.
- Facility assignment/authorization remains enforced server-side.
- Offline writes carry ClientOperationId/idempotency semantics so retries do not double-record.
- Business date prefers the server-issued collector-session date; device-local time is fallback behavior, not authority.
- Mobile token storage and refresh use the established mobile security services.

Do not copy browser-cookie assumptions directly into the MAUI client.

## 14. Security-change requirements

Changes involving authentication, authorization, tenancy, secrets, tokens, MFA, password handling, cross-tenant access, or sensitive mutations require:

1. explicit threat/boundary statement;
2. review of current guards and tenant filters;
3. focused regression tests;
4. integration tests when database filters/constraints or request pipeline behavior matter;
5. confirmation that existing roles are not accidentally widened;
6. confirmation that secrets/tokens are not newly logged or exposed;
7. review of compatibility with existing accounts/tokens where applicable.

Use .agents/skills/stalltrack-security-review/SKILL.md for the procedural review checklist.
## 15. Security anti-patterns

Do not:

- authorize by hiding a button;
- accept CollectorId or tenant identity from a request when authentication supplies it;
- bypass tenant filters for convenience;
- return all tenants when tenant resolution fails;
- store raw refresh tokens;
- expose secrets in DTOs, logs, or source;
- weaken SameSite/Secure/HttpOnly behavior without a reviewed browser-flow requirement;
- create a second password/token implementation beside the established services;
- treat a page move as permission to broaden roles;
- claim generic audit logs provide a business lifecycle they do not actually persist.
