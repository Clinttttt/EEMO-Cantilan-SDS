# Online Payments Head setup incident — October 23, 2026

## Incident

Production Head Cantilan Online Payments configuration showed **Setup incomplete**, a webhook URL of `https://api:8080/api/onlinepayments/webhook/cantilan-sds`, clicking **Test connection** returned to the dashboard, and clicking **Save** appeared to do nothing. The payor-facing NCC October balance remained ₱2,400, but online checkout initiation displayed an error. The user needs to test their authorized PayMongo **live** key; no merchant key was supplied to the engineering tools.

## Root causes confirmed in current primary EEMO master

1. **Setup lost after Test**: `TestPaymentAsync` reloaded `LoadPaymentAsync`, which reset `_showSetup` to `!CanAcceptOnlinePayments`. The default LGU had `CanAcceptOnlinePayments=true` even with no configured default key. Re-rendering hid the Test result.
2. **Save modal unreachable**: the password modal was nested inside the Online Payments dashboard `else` branch, and not rendered while the setup-card branch was active. Saving set `_showPwConfirm=true` but no dialog appeared.
3. **Unroutable webhook**: `OnlinePaymentUrlBuilder` fell back to the request's internal Docker host `api:8080` when `OnlinePayments:WebhookBaseUrl` was not configured. The resulting string was HTTPS but not publicly routable.
4. **Misleading default account status**: default LGU readiness was inferred from `IsDefault` without verifying that a default merchant key existed. The UI displayed an ambiguous `Office account` badge where an obvious settings action belonged. Fallback/disconnect messaging also implied an available default account when none was configured.
5. Successful saving closed the form and concealed the success message, leaving staff unsure whether webhook registration completed.

## Focused correction

- Render re-authentication modal as a sibling of both setup and dashboard layouts; retain server-side Head password confirmation.
- Keep setup card and test results visible after connection test. Keep saving results visible after Save; use `Back to payments` and `Manage PayMongo` controls.
- Correct default-LGU readiness using the presence of the actual configured default merchant key; expose a non-secret boolean to distinguish real default fallback from an empty installation.
- Deploy public API origin in `appsettings.Production.json` as `OnlinePayments:WebhookBaseUrl=https://api.stalltrack.site`. Reject internal hostnames in production even if they begin with HTTPS.
- Preserve per-LGU own-key resolution, date/financial integrity, signature validation, tenant isolation, webhook event subscription checks and server-side password re-auth. Both real `sk_live_` and test `sk_test_` credential prefixes continue to work; no key is logged or hardcoded.

## Verification / operational limits

The focused xUnit suite initially passed 40/40 prior to the final default-fallback refinement, and the Head Release build passed with zero errors. A subsequent compile/run must verify the final DTO/fallback changes before release. The initial concurrent test+Head build produced a transient CS2012 Windows file-lock build error; the single sequential Head build passed. No production merchant credentials, transactions, existing balances, ORs, webhooks or database migrations were changed by these source fixes.

**Real PayMongo validation** still requires an authorized Head to enter its own live key and use **Test connection** / password-confirmed **Save** after compatible deployment. Test connection only calls the provider account-verification API and does not create a charge. Do not generate a real QRPh checkout or settle the NCC ₱2,400 obligation without explicit owner approval for financial-state modification.

**Queued future work:** Fish/Meat Weight & Measure LGU-specific enablement and rates (separate, not part of this release).
