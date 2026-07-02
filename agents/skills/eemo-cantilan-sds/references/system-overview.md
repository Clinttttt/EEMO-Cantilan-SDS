# System Overview

## Product

EEMO Cantilan SDS is a government revenue collection system for the Economic
Enterprise and Management Office of Cantilan, Surigao del Sur. It replaces
manual collection records with auditable digital workflows for web admins,
payors, and mobile collectors.

## Facilities

| Code | Facility | Collection model |
|---|---|---|
| NPM | New Public Market | Daily per stall, utilities, fish kilo fee |
| TCC | Tampak Commercial Center | Monthly rental |
| NCC | New Commercial Center | Monthly rental, extension/corner context |
| BBQ | Barbecue Stand | Monthly rental |
| ICE | Iceplant | Monthly rental |
| SLH | Slaughterhouse | Per head, animal type rate |
| TRM | Transport Terminal | Per trip, driver/transporter queue |
| TPM | Tabo-an Public Market | Friday market vendor fee |

## Roles

- Head/SuperAdmin: setup, admin/collector account creation, full oversight.
- Admin: web records, OR entry, reports, facility/vendor/stall management.
- Collector: mobile field collection, facility-assigned.
- Payor: portal user for linked stall balances and online payments.

## Business Rules To Protect

- OR numbers are manually entered and globally unique across transaction types.
- Collector attribution comes from authenticated actor context, never payload.
- Admin-recorded and online payments usually have `CollectorId = null`; audit captures the actor.
- NPM is daily-collection first. Do not create monthly `PaymentRecord` flows for NPM unless a verified feature explicitly does so.
- Monthly-rental facilities use the stall's stored `MonthlyRate`; range constants are not billing truth.
- Delinquency is based on unpaid months; 3+ is delinquent, 1-2 is arrears.
- Contract expiry warning is three months.
- Business-day logic uses Philippine time (`PhilippineTime`); persisted timestamps stay UTC.
- Financial mutations are audited by `AuditSaveChangesInterceptor`; handlers should not manually insert audit rows.

## Current Module Map

- Domain: entities, enums, fee/rule constants, result type, Philippine clock.
- Application: CQRS records, handlers, validators, DTOs, requests, repository/API-client interfaces, cache/tenant abstractions.
- Infrastructure: EF Core, repositories, entity configs, migrations, audit interceptor, cache implementation, PayMongo gateway, static tenant.
- API: thin controllers, auth, middleware, SignalR hubs.
- HttpClients: shared typed clients and `HandleResponse` for web/mobile.
- Client: Blazor Server admin surface and payor portal.
- Mobile.Core: platform-neutral offline read cache, pending-operation store, sync service.
- Mobile: MAUI Blazor collector app, token storage, connectivity/file-system glue.
- Testing: xUnit, Moq, FsCheck, EF in-memory support.

## Source Context

Start with `agents/CODEX_AGENTS.md`, then the Amazon Q and Kiro docs. Treat
current source as authoritative when documentation is stale.
