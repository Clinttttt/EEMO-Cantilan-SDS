# Two-Factor Authentication (MFA) — How It Works

StallTrack's two-factor sign-in uses **TOTP** (Time-based One-Time Password), the same standard behind
Google Authenticator, Microsoft Authenticator and Authy. This document explains the mental model, the exact
flows, and where each piece lives in the code.

---

## 1. The core idea in one paragraph

At setup, the server generates a random **secret** and shows it to you once (as a QR code). Your authenticator
app stores that secret. From then on, **both sides know the same secret** — and both can compute a 6-digit
number from `secret + current time`. The app shows the number; you type it; the server computes what it
expects and compares. Nothing is ever sent between the app and the server: the app works fully offline, even
in airplane mode. That is the whole trick.

> **The code is not a message.** It is not emailed, texted, or pushed. It is *calculated independently* on
> your phone and on the server from a shared secret plus the clock.

### Why this is safer

A password can be stolen and reused for months. A TOTP code is worthless seconds later, and cannot be derived
from the code you used before. An attacker needs your password **and** physical possession of the enrolled
phone.

---

## 2. Why 6 digits every 30 seconds

Time is divided into 30-second slots called **steps** (`unix_seconds / 30`). Both sides compute:

```
code = truncate6( HMAC-SHA1(secret, current_step) )
```

Same secret + same step → same 6 digits. When the step advances, the number changes. The digits look random,
but they are a deterministic function of the secret and the clock.

**Clock drift:** phones and servers are never perfectly in sync, so the server also accepts the code from
**one step before and one step after** now (±30 s). That is why a code that just expired on screen often still
works.

If someone's phone clock is badly wrong (more than ~1 minute off), their codes will be rejected. The fix is to
enable automatic date & time on the phone, not to change anything in StallTrack.

---

## 3. Setting it up (enrollment)

Where: **Settings → Security & Access Policy → Two-factor authentication**.

```
 YOU                          STALLTRACK (server)                 AUTHENTICATOR APP
  │                                   │                                   │
  │ 1. enter current password ───────▶│                                   │
  │                                   │ verify password                    │
  │                                   │ generate random secret             │
  │                                   │ store it ENCRYPTED (not enabled)   │
  │◀── 2. QR code + manual key ───────│                                   │
  │                                   │                                   │
  │ 3. scan the QR ───────────────────────────────────────────────────────▶│
  │                                   │                      stores secret │
  │◀── 4. app shows 6 digits ─────────────────────────────────────────────│
  │ 5. type those digits ────────────▶│                                   │
  │                                   │ compute expected code, compare     │
  │                                   │ MATCH → two-factor ENABLED         │
  │◀── 6. 8 recovery codes (once) ────│                                   │
```

Key points:

- **Your password is required to start.** A hijacked, already-signed-in session cannot bind an attacker's
  authenticator to your account.
- **Nothing is enforced until step 5 succeeds.** If you abandon setup (or your connection drops), the account
  is exactly as it was — you can still sign in with just your password. Internally the state is
  "secret stored, but not enabled", shown in the UI as *Setup unfinished*.
- **Starting again issues a brand-new secret**, so an abandoned attempt can never be resumed later.
- The QR code and the manual key are **the same secret** in two formats. Scanning is just a faster way of
  typing the key.

### Where do I scan? (the most common confusion)

The QR must be scanned **from inside an authenticator app**, not with the phone's camera app. The camera will
only show raw text like `otpauth://totp/EEMO%20Cantilan:head?secret=...`, which is correct but useless on its
own.

- *Google Authenticator*: **+** → **Scan a QR code**
- *Microsoft Authenticator*: **+** → **Other account** → **Scan a QR code**
- Cannot scan? In the app choose **Enter a setup key**, paste the key shown on screen, type **Time based**.

The app entry is labelled with your office and username, e.g. **`EEMO Cantilan: head`**. That label is
data-driven per LGU (office acronym + municipality), so Carmen staff see `CEEO Carmen`, and so on.

---

## 4. Signing in with two-factor on

Sign-in becomes two steps. **Critically, no session exists until the second step succeeds** — the password
alone gets you nothing.

```
 YOU                          STALLTRACK (server)
  │                                   │
  │ 1. username + password ──────────▶│
  │                                   │ password correct?
  │                                   │ MFA on for this account?
  │                                   │ → issue a short-lived CHALLENGE
  │                                   │   (5 minutes, single use)
  │                                   │   NO access token, NO cookie
  │◀── 2. "enter your code" ──────────│
  │                                   │
  │ 3. read 6 digits from the app     │
  │    (works offline)                │
  │ 4. code + challenge ─────────────▶│
  │                                   │ recompute expected code, compare
  │                                   │ MATCH → issue the real session
  │◀── 5. signed in ──────────────────│
```

Protections at the second step:

| Risk | Protection |
|---|---|
| Guessing 6 digits | Wrong codes count as failed sign-ins → the existing **5 attempts / 15-minute lockout** applies |
| Someone shoulder-surfs a code | Each code works **once**; the used time-step is recorded and refused afterwards |
| Stalling on the challenge | The challenge expires in **5 minutes** and is single-use |
| Account disabled mid-sign-in | Active status and lockout are **re-checked** at the second step |
| Probing which part was wrong | One identical error message for every failure |

Accounts **without** MFA are completely unaffected — their sign-in is byte-for-byte the previous behaviour.

---

## 5. Recovery codes (what saves you when the phone is gone)

At the moment two-factor is switched on, you receive **8 single-use recovery codes** like `4WA3H-HANMH`.

- They are shown **exactly once**. Only their hashes are stored, so nobody — including us — can display them
  again.
- Any one of them can be typed **in place of** the 6-digit code, at sign-in or when turning MFA off.
- Each works once. Generating a new set **invalidates every old code**.
- Formatting is forgiving: case, spaces and the dash are ignored.

**Store them somewhere that is not the enrolled phone** — printed and locked away, or in a password manager.
If a code is ever exposed (screenshot, chat, email), generate a new set immediately.

The panel warns when 2 or fewer remain.

---

## 6. Turning it off

Requires **both** your current password **and** a valid code (or a recovery code). Turning the second factor
off is itself protected by the second factor, so a stolen session cannot strip it.

Switching off erases the secret, the recovery codes and the replay marker — re-enrolling always starts from a
brand-new secret, so an old authenticator entry stops working. Delete the stale entry from your app.

---

## 7. What is stored, and what is never stored

| Item | How it is kept |
|---|---|
| TOTP secret | **Encrypted** at rest (AES-256-GCM) via the platform's credential protector |
| Recovery codes | **SHA-256 hashes only** — the codes themselves are unrecoverable |
| Sign-in challenge | **Hash only**, with a 5-minute expiry |
| Last used time-step | A number, to block replay of a code inside its own window |
| The 6-digit codes | **Never stored, never transmitted anywhere except your one submission** |

Every state change (enrollment started, enabled, disabled, recovery codes regenerated) is written to the
application log with the username — never a secret or a code.

---

## 8. Common situations

| Situation | What to do |
|---|---|
| App shows a code but it is rejected | Check the phone's clock is set to **automatic**. Drift beyond ~1 minute breaks TOTP. |
| Typed the setup key into the code box | The key goes **into the app**; the app then shows the 6-digit code to type into StallTrack. |
| "Your session has expired" while setting up | The page was open too long (the API token lasts 15 minutes). Refresh, sign in, retry. |
| Lost the phone | Use a recovery code to sign in, turn MFA off, then re-enrol on the new phone. |
| Lost the phone **and** the recovery codes | Ordinary admins: the Head can help. A **Head** currently has no self-service route — see the gap below. |
| Changed phones | Turn MFA off (old phone still works), then set it up again on the new one. |
| Deleted the app entry by accident | Same as losing the phone: use a recovery code. |

---

## 9. Current scope and known gaps

**Implemented:** opt-in enrollment for web admin/Head accounts, QR + manual key, sign-in enforcement,
recovery codes, replay protection, lockout integration, per-LGU labelling.

**Not yet implemented:**

- **Mandatory MFA for Heads** — currently opt-in for everyone.
- **Platform-operator MFA reset** — the agreed rescue path for a Head who loses both their phone and their
  recovery codes. Until this ships, that situation means a **permanent lockout** of that account, so keep
  recovery codes safe.
- **Collector (mobile) MFA** — out of scope by design; field collectors sign in on the mobile app.
- **"Remember this device"** — deliberately not offered; a code is required at every sign-in.

---

## 10. Where the code lives

| Concern | Location |
|---|---|
| TOTP algorithm (RFC 6238) | `EEMOCantilanSDS.Infrastructure/Security/TotpService.cs` |
| QR rendering | `EEMOCantilanSDS.Infrastructure/Security/QrCodeGenerator.cs` |
| Secret encryption | `EEMOCantilanSDS.Infrastructure/Security/AesCredentialProtector.cs` |
| Account state + rules | `EEMOCantilanSDS.Domain/Entities/Users/BaseUser.cs` |
| Enroll / confirm / disable / regenerate | `EEMOCantilanSDS.Application/Command/Auth/Mfa/MfaCommandHandlers.cs` |
| Recovery code generation | `EEMOCantilanSDS.Application/Common/Security/RecoveryCodes.cs` |
| Sign-in gate (password step) | `EEMOCantilanSDS.Application/Command/Auth/AdminAuth/Login/LoginCommandHandler.cs` |
| Sign-in second step | `EEMOCantilanSDS.Application/Command/Auth/Mfa/VerifyMfaLoginCommandHandler.cs` |
| Endpoints | `EEMOCantilanSDS.Api/Controllers/AdminAuthController.cs` (`mfa/*`) |
| Settings UI | `EEMOCantilanSDS.Client/Components/Pages/Shared/TwoFactorPanel.razor` |
| Login UI (code step) | `EEMOCantilanSDS.Client/Components/Pages/Login.razor` |
| Tests | `EEMOCantilanSDS.Testing/Infrastructure/Security/TotpServiceTests.cs`, `EEMOCantilanSDS.Testing/Application/Auth/Mfa*Tests.cs` |

The TOTP implementation is verified against the **official RFC 6238 test vectors**, which is what guarantees
any standard authenticator app agrees with it.
