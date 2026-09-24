# StallTrack Testing Strategy

**Status:** Canonical test-layer guidance.

StallTrack uses separate test layers because correctness spans pure domain rules, rendered Blazor behavior, and PostgreSQL-specific persistence/tenancy behavior.

## 1. Test projects

- EEMOCantilanSDS.Testing — xUnit unit/domain/application/repository-oriented tests.
- EEMOCantilanSDS.ComponentTests — bUnit rendered Blazor component tests.
- EEMOCantilanSDS.IntegrationTests — PostgreSQL/Testcontainers integration tests.

Run the normal suites separately; combining them has historically caused a bUnit timing flake.

## 2. Choose the test by the failure mode

Use unit/domain tests for:

- pure calculations and invariants;
- command/query handler behavior with mocked ports;
- authorization guards with no database translation dependency;
- date/rate/occupancy rules.

Use component tests for:

- route metadata;
- authorization attributes;
- rendered labels/statuses;
- filter/action behavior;
- link destinations;
- loading/empty/error states;
- conditional UI.

Use PostgreSQL integration tests for:

- global query filters;
- tenant isolation;
- database constraints/indexes;
- EF Core translation;
- migrations;
- transaction/concurrency behavior that mocks cannot prove.
## 3. High-risk change rule

Money, reporting, tenancy, authorization, and authentication changes require a focused regression test.

The test should fail under the defect or unsafe behavior being corrected. Where practical, prove this by temporarily reintroducing the defect locally during development/review.

Do not add broad snapshot tests when one focused behavior test can state the invariant directly.

## 4. Financial tests

Financial tests should name the business basis they prove:

- tenant;
- facility/source;
- occupancy/term;
- obligation period;
- effective-rate date;
- collection date where relevant;
- current versus historical scope.

Do not assert two figures are equal until their definitions are equal.

NPM tests must preserve configured month-basis semantics rather than reproducing arithmetic in the test/UI.

## 5. Tenant tests

For tenant-sensitive behavior, include cases for:

- current tenant data visible;
- another tenant data hidden;
- unresolved tenant with an accessor fails closed;
- deliberate no-accessor/tooling behavior where applicable;
- tenant-scoped uniqueness.

Use integration tests when the guarantee belongs to EF/database behavior.

## 6. UI tests

Prefer rendered behavior over raw source-string assertions.

A route migration should prove:

- canonical route;
- compatibility route where required;
- one intended route owner;
- authorization unchanged.

A terminology change should prove the user-visible state, including empty/error states when the old wording could remain there.
## 7. Validation commands

Typical release validation:

    dotnet build EEMOCantilanSDS.slnx --configuration Release

    dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release

    dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release

    dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release

The integration suite requires Docker/Testcontainers.

For bounded slices, run focused tests first, then the relevant full suite.

## 8. Documentation-only changes

Documentation migrations that alter paths referenced by tests or comments must update those references.

A documentation-only change should still run:

- git diff --check;
- path/link validation;
- any focused tests that read canonical documentation files.

It does not require production deployment merely because documentation moved.

## 9. Test ownership

Tests belong with the behavior they protect. Do not move a business invariant into a component test merely because the defect appeared in the UI.

Likewise, do not require database integration for a pure rendered-label change unless persistence behavior is part of the claim.

Test names should describe the invariant, not the implementation detail used to exercise it.
