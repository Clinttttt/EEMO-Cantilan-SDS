# Platform Operator setup — local read-only diagnostic, 2026-10-15

Evidence accompanying [the Fish / Meat UI handoff](FISH_MEAT_REGISTRY_UI_REFINEMENT_20261015.md). No authentication, setup, account, password, database or Platform Admin source change is included.

## Verified local configuration

- Platform Admin development configuration in `C:\dev\stalltrack\platform\apps\admin\src\app\core\api.config.development.ts` targets `http://localhost:5123`.
- The existing API process serves both HTTP 5123 and HTTPS 7597. Both `/api/platform-setup/status` responses returned `isSetupRequired: true`.
- Its configured local database is `localhost:5432 / EEMOCantilanSDS.DB`. The running API's PostgreSQL TCP client port was matched to `pg_stat_activity`, confirming that database rather than relying solely on a configuration file.
- Configuration credentials were read internally only; no password, password hash, token or connection-string secret was printed or changed. Database access was restricted to localhost, read-only transactions, and rollback.

## Account evidence and cause

The owner identified possible usernames `admin` or `clint`.

| Recorded fact | Result |
| --- | --- |
| Exact case-insensitive username `admin` | Found; active, not deleted |
| User ID | `76bfb0c1-995d-4ce3-838b-7f128f85fde3` |
| User type / role | AdminUser / SuperAdmin (municipal Head role) |
| Municipality | Cantilan, `0ea98ba6-f7b1-4928-96f1-6177d45741bd` |
| `IsPlatformOperator` | `false` |
| Created metadata | 2026-06-12 UTC, CreatedBy `System` |
| Exact case-insensitive username `clint` | No match in this database |
| Non-deleted users with `IsPlatformOperator = true` | **0** |

The setup handler, `EEMOCantilanSDS.Application/Queries/Auth/GetPlatformSetupStatus/GetPlatformSetupStatusQueryHandler.cs`, checks for a non-deleted AdminUser with that explicit flag. A SuperAdmin role or a username of `admin` does not establish platform authority. Therefore `true` is consistent with the current records; this is not evidence that the browser is ignoring an existing operator flag.

`PlatformOperatorPolicy` requires the dedicated operator flag. The token service stamps the corresponding claim only for flagged accounts; the API applies the same policy. Platform login uses `RequirePlatformOperator`, and municipal Head sign-in separately rejects dedicated platform accounts. The default-municipality Head fallback is intentionally not an operator grant.

`CreateFirstConsoleAdminCommandHandler` explicitly creates a dedicated flagged operator. Its default municipality is tenant context, so municipality membership alone cannot prove or disprove platform authority. The distinction is the recorded dedicated operator flag and verified onboarding intent.

Retained audit metadata queried for the candidate ID and onboarding-related actions showed AdminUser updates in August, September and October. That scoped query did not establish an original dedicated-operator creation. It is insufficient evidence for promoting this municipal Head account. Other databases and historical backups were not searched or altered.

## Correction boundary

No legitimate existing dedicated operator has yet been verified. Do not create a second operator, promote `admin`, reset a password, copy hashes, bypass setup, or change `isSetupRequired` based on this diagnostic.

The next step is to establish the intended original platform operator from owner/onboarding evidence and confirm its exact database and immutable user ID. Only if that evidence proves a metadata defect should a separate owner-approved correction be designed: one exact account, explicit old/new flag, platform security review, audit reason/actor, concurrency check and post-change boundary verification. No manual SQL or account mutation is authorized by this handoff.

## Separate Fish / Meat address evidence

The local Josephine Reyes registration (`e28436e7-03fb-4420-a744-812f7bebe855`) records BusinessName `reyes fishland` and Address `San Vicent Carmen SurigaoSan Vicent Carmen Surigao` verbatim. The repeated substring is already stored. The former Manage secondary line also appended that address despite having a dedicated Address column; the UI fix removes only that second rendering.

The record was not rewritten. Verify the intended address against the office source, then use a separately authorized audited profile correction. The inspected OfficeSources API has registration, import, renewal and closure operations but no general vendor-profile correction endpoint; a new registration or renewal should not be used as an address-cleaning shortcut. This diagnostic cannot determine whether the original repeated value was typed or uploaded.

The six optional office columns and EF migration `20261014145610_FishMeatOfficeRegisterFields20261014` were already present in the local database at inspection. The earlier office-register handoff's migration-pending statement is a historical snapshot, not an instruction to apply it again. This task ran no migrations and did not edit protected release documents.

Local diagnostic evidence is outside Git at `C:\dev\stalltrack-v2-worktrees\fish-meat-registry-qa\diagnostic\read-only-results*.txt`. Findings describe this local snapshot, not production state.
