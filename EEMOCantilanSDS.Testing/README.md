# EEMOCantilanSDS.Testing

Tests are grouped by the layer or concern they verify:

- `Application/` - command handlers, validators, and application services.
- `Domain/` - domain entities, value behavior, and business rules.
- `Infrastructure/` - repositories, interceptors, persistence, and data access behavior.
- `Regression/` - bug reproductions and preservation tests for previously fixed behavior.
- `Shared/` - test base classes and reusable test helpers.
- `Smoke/` - minimal placeholder or smoke tests.
- `Utilities/` - cross-cutting helper tests.

Keep new tests close to the layer and feature they protect. If a test documents a bug fix, prefer `Regression/<Feature>/`.
