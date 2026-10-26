# Revenue Sources: add operations after onboarding; neutral facility names (2026-10-26)

## 1. Available to add (Settings > Revenue Sources)

A municipality can add a catalog operation it did not select at onboarding (ECF, WCF, Weight & Measure, Transportation / Parking, Market Fees, Landing / Berthing, Transfer Large Cattle, Fish / Meat Vendor Fee, the space rentals). Reused: `TenantOperationCatalog` (the stable identity and neutral name), `TenantOperationSetting` (listing row, optional local name, optimistic `Version`), `OperationSettingsController`, `RevenueClassification` (the source), and the Collector app's own capability evaluation.

**Add** (`POST api/operation-settings/{code}/add`, Head only, `AddTenantOperationCommand`): one transaction lists the operation (and sets the optional local name, validated by the existing `LabelProblem` rules) and creates its revenue source *only if none exists*. A source that already exists, active or retired, is reused as it is: never duplicated, and a **retired source is never reactivated** (the result says so and the operation stays Setup required). It is idempotent (a repeat finds it listed and changes nothing; a competing add that won is the same outcome), bound to the row `Version` (stale = conflict, nothing written), tenant-scoped from the token, and a municipality that never adopted configuration (every operation already listed) writes nothing.

**It authorizes nothing.** No policy / instrument, collector assignment, activation, rate, document or Collection is created. The operation then appears in the directory and the Operations menus by the existing listing rules, but Mobile shows it only to a collector the Head assigns, and only collectible once the existing gates pass.

**State, from backend facts** (`GET api/operation-settings/setup`, `GetOperationSetupQuery`): *Available* (not listed), *Added* (listed, source and an effective policy exist, no collector assigned yet), *Setup required* (a missing/retired source, no effective policy, or what an assigned collector still needs: each reason is the Collector app's own reason code in plain words), *Ready to collect* (at least one assigned active collector is told, by `GetCollectorOperationCapabilitiesQueryHandler`, that it is collectible now). That handler's body was extracted unchanged into `BuildForCollectorAsync(tenant, collector)` so the Head's verdict and the collector's app cannot disagree; the collector endpoint behaves as before.

UI (`RevenueSetup.razor`): a **Collections** register of added operations with the state chip and gaps, an **Available to add** list with an Add dialog (optional local name, a note that nothing is authorized), and a notice after adding. The old name-only "Setup required" list is shown only when the verified state cannot be read.

## 2. Neutral suggested facility names

`FacilityCatalog.Defaults` (only used by Facility Configuration's Available to add): TCC "Commercial Center" (was "Tampak Commercial Center"), TPM "Weekly Market" (was "Tabo-an Public Market"), NPM "Public Market" (was "New Public Market"). NCC stays "New Commercial Center", so TCC and NCC remain distinct; codes, short names and billing archetypes are unchanged. Names are only suggestions: a facility already configured keeps its stored name, rates, contracts and history.

## Verified

PostgreSQL `OperationSetupTests` (5): add lists + creates the source and nothing else (no policy, assignment, activation or Collection); an existing source is reused and a retired one stays retired; a repeat, a stale version and a name that reads as another operation are handled; a non-Head is refused, another municipality is untouched, an un-adopted municipality writes nothing; Added -> Ready -> Setup required (collector deactivated) for Kanmanggay. Component `RevenueSetupTests` (3 new, 13 existing). Unit `GetFacilityConfigurationQueryHandlerTests` (neutral names, TCC != NCC).

## Limits

- Ready is per collector capability (Mobile). An operation collected only at the office shows Added or Setup required until a collector is assigned; the page says so.
- The retired-source case needs a Head/operator decision outside this screen (a new policy cannot revive a retired source).
- `FacilityDisplayNames` (payor notification wording) still carries the old Cantilan names; it is a different surface and was not changed.
