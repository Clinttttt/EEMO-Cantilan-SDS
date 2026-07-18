# StallTrack Collector — App Distribution Site (`app.stalltrack.site`)

Static site that hosts the collector APK download, the **bind-link** landing page (`/a/{token}`), and the
Android App Links verification file (`/.well-known/assetlinks.json`). This is **Unit 3** of the mobile
publishing / bind-link stage. It is purely static — nothing here touches the running API/web/DB.

```
mobile-app-site/
├── index.html                     landing + download + bind-link ("invited") page
├── staticwebapp.config.json       routes, /a/{token} fallback, .apk MIME type
├── .well-known/
│   └── assetlinks.json            App Links proof (needs the release signing fingerprint)
├── assets/
│   └── seal.png                   (add) platform seal shown in the hero; falls back to a monogram
└── download/
    └── stalltrack-collector-latest.apk   (add) the signed release APK
```

The bind/download URLs the Head shares come from the API (`Mobile:AppBaseUrl`, default
`https://app.stalltrack.site`; `Mobile:DownloadUrl`, default
`https://app.stalltrack.site/download/stalltrack-collector-latest.apk`). Keep this site's paths in sync
with those, or override the two app settings.

---

## Prerequisites (yours to provide)

1. **DNS** — a subdomain `app.stalltrack.site` you can point at the host (same as `console` / `api`).
2. **A release signing keystore** — required to sign the APK **and** to compute the `assetlinks.json`
   fingerprint. App Links only auto-open the app when the installed APK is signed by the key whose
   SHA-256 is listed in `assetlinks.json`. Use one stable keystore for all releases.

---

## Step 1 — Build + sign the release APK

The app id is already `site.stalltrack.collector`.

```powershell
# From the repo root. Produces a signed APK when the keystore properties are supplied.
dotnet publish EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -c Release -f net10.0-android `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=C:\path\to\stalltrack-release.keystore `
  -p:AndroidSigningStorePass=<store-password> `
  -p:AndroidSigningKeyAlias=<alias> `
  -p:AndroidSigningKeyPass=<key-password>
```

The signed APK is written under
`EEMOCantilanSDS.Mobile/bin/Release/net10.0-android/publish/site.stalltrack.collector-Signed.apk`.
Copy it to `mobile-app-site/download/stalltrack-collector-latest.apk` (keep a versioned copy too, e.g.
`stalltrack-collector-1.0.0.apk`).

> Don't have a keystore yet? Create one once and keep it safe (losing it means you can't ship updates
> that upgrade the same install):
> ```powershell
> keytool -genkeypair -v -keystore stalltrack-release.keystore -alias stalltrack `
>   -keyalg RSA -keysize 2048 -validity 10000
> ```

## Step 2 — Get the SHA-256 fingerprint → fill in `assetlinks.json`

```powershell
keytool -list -v -keystore C:\path\to\stalltrack-release.keystore -alias <alias>
```

Copy the **SHA256** line (e.g. `AB:CD:...:EF`) and paste it into
`.well-known/assetlinks.json`, replacing `REPLACE_WITH_RELEASE_SIGNING_SHA256_FINGERPRINT`. The colons
are fine; multiple fingerprints are allowed (e.g. if you later also use Play App Signing — add Google's
upload/app-signing SHA-256 here too).

## Step 3 — Deploy the static site

**Option A — Azure Static Web Apps (recommended; handles the `/a/{token}` fallback + free TLS):**

```powershell
# One-time: create the SWA (Free tier), no framework build.
az staticwebapp create -n stalltrack-app-site -g stalltrack-prod-rg -l eastasia --sku Free

# Deploy the folder (via the SWA CLI):
npm i -g @azure/static-web-apps-cli
swa deploy ./mobile-app-site --deployment-token <token-from-portal> --env production
```

`staticwebapp.config.json` already rewrites unknown paths (like `/a/{token}`) to `index.html`, serves
`.apk` with the correct MIME type, and returns `assetlinks.json` as JSON.

**Option B — Azure Blob static website + CDN/Front Door:** enable `$web`, upload the files, set the
404 document to `index.html` (so `/a/{token}` lands on the page), and ensure `.apk` is served as
`application/vnd.android.package-archive` and `/.well-known/assetlinks.json` as `application/json`.
(Blob's `$web` doesn't serve dot-folders by default — Front Door / a rewrite rule may be needed for the
`.well-known` path; SWA avoids this, hence the recommendation.)

## Step 4 — Point DNS

Add a CNAME `app` → your SWA/CDN hostname, then add the custom domain in the host (SWA: *Custom
domains*). Wait for TLS to provision.

## Step 5 — Verify

- `https://app.stalltrack.site/.well-known/assetlinks.json` returns the JSON (content-type
  `application/json`).
- `https://app.stalltrack.site/download/stalltrack-collector-latest.apk` downloads the APK.
- `https://app.stalltrack.site/a/anything` shows the landing page with the "invited" banner.
- App Links check (device with the release build installed):
  ```
  adb shell pm verify-app-links --re-verify site.stalltrack.collector
  adb shell pm get-app-links site.stalltrack.collector
  ```
  The domain should show `verified`. Tapping a real bind link then opens the app directly.

---

## How it ties together (end-to-end)

1. Head opens **Collectors → Collector app link**, copies the **bind link**
   (`https://app.stalltrack.site/a/{token}`) and the **download link**, shares them with collectors.
2. Collector taps the download link → this page → installs the APK.
3. Collector taps the **bind link** → App Links opens the app → `MainActivity` hands the URI to
   `MobileBindBridge` → the app calls `GET /api/mobile/bind/{token}` and stores its LGU.
4. The login screen shows **no picker** and signs in scoped to that LGU. (If the app isn't installed,
   the bind link lands here instead, with install instructions.)

Security: the bind link is presentation + login-scoping only — **not** a security boundary. Login and
LGU-scoped accounts remain the real gate, so a mis-bound app still cannot read another LGU's data.

## In-app update prompt (v2)

The app checks `GET /api/mobile/version` on launch (post-login, once per session) and compares its
installed Android `versionCode` against the server's. It's **config-driven** on the API — defaults report
version 1 / no minimum, so nothing is ever prompted until you bump the settings:

| App setting | Meaning | Default |
| --- | --- | --- |
| `Mobile:LatestVersionCode` | latest published `versionCode`; `> installed` ⇒ "update available" | `1` |
| `Mobile:LatestVersion` | display version (e.g. `1.1.0`) | `1.0` |
| `Mobile:MinSupportedVersionCode` | `> installed` ⇒ update is **mandatory** (blocking) | `0` |
| `Mobile:DownloadUrl` | APK URL the prompt opens | `{AppBaseUrl}/download/stalltrack-collector-latest.apk` |
| `Mobile:UpdateNotes` | optional text shown in the mandatory prompt | — |

After publishing a new APK: set `Mobile:LatestVersionCode` to the new build's `versionCode` (and
`Mobile:MinSupportedVersionCode` if you want to force it). Side-loaded APKs can't self-install silently, so
the app opens the download link and the user taps to install (prompted, not silent — that's an Android
constraint for non-Play installs).

## CI/CD APK build (`.github/workflows/publish-apk.yml`)

Manual **workflow_dispatch** job (never runs on push/PR, so it can't affect the API/web deploy). It builds
+ signs the release APK and uploads it as an artifact. Add these repository secrets first:

| Secret | How to produce |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | `base64 -w0 stalltrack-release.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | keystore store password |
| `ANDROID_KEY_ALIAS` | key alias |
| `ANDROID_KEY_PASSWORD` | key password |
| `GOOGLE_SERVICES_JSON_BASE64` | `base64 -w0 EEMOCantilanSDS.Mobile/Platforms/Android/google-services.json` |

Then run the workflow (optionally passing a new `versionName` / `versionCode`), download the artifact, drop
it at `download/stalltrack-collector-latest.apk`, redeploy this site, and bump `Mobile:LatestVersionCode`.
The workflow has a commented placeholder where you can automate the upload + setting bump once hosting
exists.

## Notes / open items

- `assetlinks.json` fingerprint must match the **actual** signing key of the distributed APK, or links
  won't auto-open (they'll fall back to this page in a browser — still usable, just not seamless).
- iOS is not covered (hosted APKs are Android-only; iOS needs App Store / TestFlight — a later path).
- Auto-update is **prompted**, not silent, for side-loaded APKs — that's Unit v2 (`GET /api/mobile/version`).
