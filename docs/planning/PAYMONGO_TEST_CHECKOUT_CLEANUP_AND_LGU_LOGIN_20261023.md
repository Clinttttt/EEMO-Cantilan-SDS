# PayMongo abandoned Test checkout / multi-LGU sign-in, 2026-10-23

## Business incident and non-destructive remedy

A Cantilan payor opened a PayMongo Hosted Checkout for an NCC ₱2,400 October balance and stopped at the QR screen. This is an **unfinished Checkout Session**, not payment evidence. The Head later attempted to replace a stored PayMongo Test key with a Live key. StallTrack correctly refused merchant-key rotation while gateway-backed `Initiated`, `Pending` or `ReconciliationRequired` records exist.

According to PayMongo Hosted Checkout documentation, QR expiration **does not expire the Checkout Session**. An active session remains payable until the merchant explicitly expires it; the expire endpoint refuses when any payment is paid, pending or processing. We do **not** delete records, dismiss a provider failure as unpaid or modify the ₱2,400 obligation.

## Implemented (explicit Head action; never automatic during Save)

- Online Payments setup shows **Close abandoned Test checkouts** when the saved account is in Test mode.
- Head is re-authenticated with their own password by the API, not only by the UI.
- Handler runs only for a signed-in SuperAdmin's own municipality; tenant filter and explicit municipality predicates both remain in effect.
- Refuses non-Test merchant credentials. Refuses all cleanup while any `ReconciliationRequired` payment for this LGU exists. Limits one action to 50 gateway-backed pending/initiated transactions.
- Calls PayMongo's POST `checkout_sessions/{id}/expire` on the **saved Test** account. On success or provider HTTP 400, re-reads GET `checkout_sessions/{id}` and requires an exact matching reference and `expired` status before marking StallTrack `Expired` with limited audit evidence. Paid, active, processing, unverifiable and inaccessible sessions remain protected.
- Uses the existing OnlinePaymentRepository for tenant-scoped reads and a conditional database update; no new EF dependency in Application or schema migration.
- Preserves `Paid`, `Completed`, `ReconciliationRequired`, OR, rate or contract records; never changes financial amounts. After PayMongo confirms expiry, the repository issues a single tenant-scoped PostgreSQL compare-and-set update requiring the exact checkout ID/reference and a still-unpaid `Pending`/`Initiated` status. A captured payment or other concurrent transition is counted as blocked rather than overwritten. This does not depend on an `xmin` token (the transaction model has no such mapping).
- After closing eligible Test sessions, the Head separately uses existing **Test connection** and password-confirmed **Save** for the replacement Live key.

## Login improvement

Generic Head `/login` previously sent an empty municipality code to an arbitrary global username lookup. After onboarding multiple active LGUs, duplicated usernames can resolve to the wrong tenant. The generic login now offers a **Municipality** selector only when more than one active LGU exists; chosen Code is sent as `MunicipalityCode` to the **already-existing scoped API login**. Existing `?lgu=...` links and single-LGU screens remain unchanged. Remembered branding is never silently treated as tenant authorization; password hashing, lockout, MFA and token issuance are unchanged.

### Follow-up: clean login without an office chooser (2026-10-23)

The initially deployed always-visible municipality selector was rejected as unnecessary friction. The Head login again shows only username and password. An unscoped login now checks **all** non-deleted admin accounts sharing the username, verifies their password hashes, and authenticates only when **exactly one** account matches. It never assumes a remembered logo, typed username prefix, or the first query result identifies the tenant. If two accounts share the same username **and password**, login fails closed and advises using an existing office-specific `?lgu=` link; no selection UI, list of accounts, cookie or token is returned. Invalid passwords retain failed-attempt protections; explicit scoped login, MFA, and console-operator boundaries remain in place. No mobile selector was introduced.

## Safety, deployment and acceptance

- Do not use this feature on Live checkouts. A Test key and a test-labeled checkout are not proof that a QR Ph scan cannot send real funds; never initiate a payment during cleanup.
- Verify PayMongo's state before allowing any merchant switch; if blocked count > 0, do not bypass the protection.
- The Head's username/password remains private. The deployment does **not** reset or change an account password; the source-level duplicate-username condition is a plausible cause, not proof of any particular user's rejected sign-in.
- Rotate the Live PayMongo secret key previously exposed in a screenshot before saving new credentials.
- No schema migration is required. Production validation requires an authorized Head to sign in, explicitly invoke checkout cleanup, inspect the message and separately re-save the new key. No credentials or production transactions should be exported into development tests.

Provider references: https://docs.paymongo.com/docs/payment-channels-key-concepts and https://docs.paymongo.com/docs/payment-channels-hosted-checkout .
