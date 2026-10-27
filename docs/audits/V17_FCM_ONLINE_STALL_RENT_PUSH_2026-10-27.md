# V17 Firebase push and online Stall Rent notifications — 2026-10-27

## Production-verified root cause

- The live API emitted `FCM push disabled: no Firebase credential configured` on a real `POST /api/Notifications/collectors/{id}/send` request at 2026-10-27T02:20:08Z.
- Production had **one** `CollectorDeviceTokens` row owned by **one** registered collector. The Head Portal's existing zero-recipient message falsely claimed this meant no registered device.
- The Android app includes the FCM service, token bridge, notification channel, Android 13+ notification permission, and device token API registration.
- One existing service-account JSON was found on the authorized Windows workstation with the **same Firebase project ID as the Android app**. Its private key was not printed, uploaded, or committed. Installing that secret in production was **not completed**, because secure secret transfer via available tooling could not be completed.
- AWS Secrets Manager and SSM Parameter Store list calls in ap-southeast-1 returned **no Firebase/FCM-named secrets**. Source repo does not contain any required Admin service-account credential.

## Online payment end-to-end trace

- `OnlinePaymentsController` delegates signed PayMongo callbacks to `HandlePaymentWebhookCommandHandler`.
- The handler verifies the correct tenant's webhook secret before settlement and pins the tenant context. Repeated already-settled transactions are no-op success.
- `OnlinePaymentSettlementService` commits the paid payment and calls `IOnlinePaymentNotifier` afterward.
- `CompositeOnlinePaymentNotifier` fans out to tenant-scoped SignalR and `CollectorPushOnlinePaymentNotifier`.
- `CollectorPushOnlinePaymentNotifier` resolves the paid stall's facility, resolves **active assigned collectors through tenant-filtered repositories**, then pushes the amount/facility/OR-prompt to their FCM tokens.
- Existing push is **best effort, not a durable outbox**; lack of FCM credential, unregistered devices, expired tokens, notification permission, or external FCM faults can prevent push, without reversing the financial settlement.
- Scope for online-payable operations must remain aligned with existing approved online monthly Stall Rent business rules, not be broadened to utility, fish/market, or arbitrary operations by this task.

## Focused local changes

- Monthly canonical and legacy paths: cache invalidation errors now cannot suppress the subsequent push-notification attempt. A committed monthly payment remains successful even after cache refresh failure, and a duplicate callback does not re-notify.
- Head Portal Collector page: delivery count zero now has a neutral diagnostic explanation rather than the incorrect claim that the collector has never signed into the app.
- Added focused tests for post-commit cache-failure handling and assigned active-collector routing; no changes to gateway signature verification, financial amounts, settlement authority, payment status, OR/SRC generation, tenant resolution, or Firebase credential handling.

## Verification and operational limits

- Focused `OnlinePaymentSettlementServiceTests`: **18 passed, 0 failed**.
- Focused `CollectorOnlinePaymentPushTests`: record actual outcome separately.
- Component/API builds and full release regression: run if available; do not claim behavioral verification without a signed live test.
- Production API: **NOT updated by this patch**. No new credentials installed, no container restarted, no PayMongo settlement simulated, no financial data modified.
- The matched service-account key must be provided to the live API through a protected root-managed, read-only secret path (per-app Linux UID readable, not embedded in images, logs, Git, or container environment variables), with a controlled API-only restart and smoke. Do not copy credentials through chat or expose JSON content.
- Android device verification must test foreground/background/closed app and notification permission; one registered FCM token does not itself prove receipt.
- Update notification `Mobile__LatestVersion` is a separate operation and is intentionally **not** part of this change.

## Completion gate

Require verified FCM API initialization, nonzero successful FCM delivery to the correct tenant/assigned collector, actual Android tray receipt, no cross-tenant push, and no duplicate push on a repeated signed webhook. Never fabricate a payment in production for testing.
