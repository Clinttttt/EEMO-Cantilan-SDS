# PayMongo multi-LGU security and Online Payments OR queue — 2026-10-23

## Owner requirement
Before publishing the still-local Custom Facility category rollout, verify and harden the per-LGU Online Payments connection and webhook pipeline. The Web Admin should let staff **hide/show the Awaiting OR list** on the Online Payments page. This is a **presentation** option only; it does not waive the LGU's OR issuance duty, change accountable form records, remove the queue, or modify collected cash.

## Implemented locally on primary EEMO master

- `HandlePaymentWebhookCommandHandler`: bind signature provenance to the owning municipality of the globally resolved checkout reference; named-tenant callbacks reject cross-LGU transactions; legacy tenant-less callback rejects any transaction not owned by the default municipality; fail closed if the municipality is missing. Underlying amount integrity, idempotent settlement and OR controls unchanged.
- `PayMongoAccountVerifier`: fail closed if webhook listing is unavailable/invalid instead of blindly creating duplicates, reject existing webhook without `checkout_session.payment.paid`, reject a failed re-enable, and reject a newly created webhook explicitly reported disabled.
- `PayMongoPaymentGateway`: select a payment with actual `status=paid` from a checkout's payment attempts instead of `payments[0]`. Missing paid evidence is never treated as settled. Provider retrieval failure logs redact raw gateway responses instead of outputting potential payor PII.
- `SetMunicipalityPaymentCredentialsCommandHandler`: existing local checkout references in `Pending`/`Initiated`/`ReconciliationRequired` prevent account clearing/merchant rotation until reconciled; matching is limited to the caller's municipality. If PayMongo reveals a signing secret and it disagrees with one manually entered, return a saved-but-incomplete result without pretending it is usable.
- `MunicipalityProfileController`: `PUT api/municipality-profile/payment` now independently verifies the current Head password before invoking credential modification; missing or wrong password cannot save, rotate, or disconnect accounts. Blazor prompts at the actual save/disconnect action and forwards proof in the request. Existing Role=SuperAdmin controller guard remains.
- `OnlinePayments.razor`: responsive Hide OR list / Show OR list control with count retained; online payment dashboard and Recent Payments remain visible. Configuration status is "Configured - verify webhook" rather than incorrectly claiming live delivery. Non-default LGU account removal is labeled Disconnect account; no fallback to the default municipality's merchant account.
- No new DB migration required specifically for these payment hardening changes. Existing planned Custom Facility category EF migration is still **not production-applied**.

## Tests and evidence

- Payment-filtered .NET unit suite: **118/118 passed**, including new named-tenant cross-settlement rejects, tenant-less default restriction, same-tenant success, disabled/subscription-missing/list-error provisioning, signed-secret mismatch, merchant rotation while checkout pending, paid attempt extraction, and missing/wrong-password controller check.
- Full .NET unit suite: **3,096/3,096 passed**.
- Head Portal Release build: **0 errors** (94 existing warnings).
- Full Head components: **1,416/1,416 passed** (0 failed).
- Public production API health GET 200 and webhook GET 405 were observed in the earlier read-only audit, **not** a real provider delivery/checkout proof.
- The user's unrelated modified Platform `apps/landing/src/app/core/municipalities.registry.json` remains untouched.
- PayMongo docs confirm `checkout_session.payment.paid` carries all attempts in `payments`, at most one paid; the event should be signed and checked for merchant provenance. References: https://docs.paymongo.com/docs/developer-tools-webhooks-events and https://docs.paymongo.com/docs/payment-channels-hosted-checkout .

## Remaining release/verification constraints

1. No production deploy, payment, real merchant setup, live key rotation, webhook registration, OR issuance or migration was performed. Production is not verified to have active LGU-specific AES `Encryption:Key`, public HTTPS webhook base URL, and supported PayMongo merchant accounts. Check **configuration presence without printing values** before release.
2. Require a two-LGU test-mode acceptance: Head password authorization -> save key -> create/reuse webhook -> sign simulated wrong-LGU and valid-LGU deliveries -> successful checkout with mixed payment attempts -> ledger settlement -> OR awaiting queue -> official OR issue -> report reconciliation -> avoid duplicate processing.
3. Client/API for server-side password verification must be released **atomically** because the payment settings PUT contract gains `CurrentPassword`. Legacy clients without proof will receive 400; do not deploy API alone if heads are still using an old compiled Head client.
4. PayMongo webhook signing-secret presence proves storage only. UI intentionally says verification is still required; do not claim a provider delivery verified unless a test delivery is observed.
5. Legacy default municipality uses the tenant-less signed webhook; nondefault LGUs must always supply own merchant keys and own webhook endpoints. Never permit cross-LGU fallback.
6. Account removal preserves previously captured financial history; it does **not** delete webhooks at PayMongo, because deleting a provider resource can be consequential and requires separate lifecycle design.
7. New facility category rollout remains staged locally, with its EF migration and official classification review distinct from this payments task.
