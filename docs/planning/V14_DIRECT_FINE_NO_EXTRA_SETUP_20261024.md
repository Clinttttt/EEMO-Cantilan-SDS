# V14 Head Portal direct-fine manual collection — 2026-10-24

## Explicit product decision
Head Portal SuperAdmin can use Record direct fine without first creating DirectFineAuthority, selecting a registered account or creating an assessment. The workflow remains separate from previously assessed fines and does not change their balances. User explicitly authorized this direct-recording behavior. No automatic creation of fake authority, governing references, approval records or penalty definition versions.

## Implementation
- The existing DirectFineOptionsDto always includes a built-in Direct fine option with sentinel Guid.Empty and requires a free-text reason. Existing independently approved category options remain supported with their original fixed/max rules and evidence constraints.
- The backend validates positive amounts in centavos, current Philippine business date, up-to-1000-character reason, optional 200-character supporting reference and payer particulars, SuperAdmin tenancy/identity, and the existing RevenueClassificationCodes.PenaltiesAndFines + OfficialReceipt instrument policy. It does NOT bypass mandatory OR classification setup or create a physical OR/accountable document.
- An ad-hoc fine is posted only through CanonicalCollectionPostingCoordinator inside the existing serializable transaction, with SourceKind and SourceId both null (the database's existing allowed generic canonical-line shape, hence no migration). Its dedicated immutable PostingOperation.Origin = DirectFineCollection plus actor/intent fingerprint and audit snapshot explicitly identify the manual source. Never fabricate a DirectFineAuthority FK.
- The Fines register includes canonical generic-source lines only when linked to an exact same-tenant PostingOperation of origin DirectFineCollection and classified PenaltiesAndFines. Both this register and its summary remain correction-aware. Existing typed authority-backed lines and reporting remain unchanged.
- The Head drawer defaults to the manual direct fine and offers amount, reason (required), optional payer/reference, then the existing money-received confirmation, immutable intent recovery and SRC outcome.

## Verified tests at checkpoint
- Targeted PostgreSQL integration: 6/6 passed. Covers manual without authority, financial reporting and Collection Activity, SRC and idempotent replay, missing OR policy/invalid amount/reason and forbidden collector, plus existing typed authority workflows.
- Focused Web component tests: 56/56 passed. Covers manual review/post and legacy authority-backed UI.
- Broader Web regression and CI need separate result confirmation before release approval.

## Safeguards and limitations
- This product mode records an ad-hoc collection; it does not establish that a new fine is legally authorized by a particular LGU ordinance. The responsible office remains accountable for applicable lawful collection/receipt requirements.
- A StallTrack SRC is not a physical OR serial. The existing OR revenue instrument policy remains mandatory; physical accountable-form issuance and reconciliation stay separate.
- No authoritative financial data, production office settings, migration, deployment, or existing receipt altered.
- Android/mobile and distinct-vendor/NPM changes elsewhere in V14 are unchanged. Keep production release blocked on CI, Android signing/version/device verification and coordinated API+Web approval.

## Additional governed-category regression
- The expanded direct-fine integration set passed 22/22 executed tests with one intentionally skipped synthetic fixture runner. The previous regression assertions were updated to ensure retired or invalid *governed* categories remain unavailable, while the separate no-authority manual option remains available. Existing retirement, fixed-amount, authority checks, reversal, idempotency, office-turnover and rollback checks still pass. No business rules were weakened merely to satisfy older tests.
