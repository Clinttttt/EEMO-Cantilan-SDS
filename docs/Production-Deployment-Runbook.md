# EEMO StallTrack Production Deployment Runbook

This guide is the repeatable release process for updating the Azure-hosted EEMO StallTrack system after a development change has been tested locally.

It covers the production environment currently used by this project:

| Component | Production resource |
|---|---|
| Resource group | `rg-stalltrack-prod` |
| Container Registry | `acrstalltrackclint2026.azurecr.io` |
| API App Service | `stalltrack-api-clint-2026` |
| Portal App Service | `stalltrack-web-clint-2026` |
| API domain | `https://api.stalltrack.site` |
| Portal domain | `https://eemo.stalltrack.site` |
| PostgreSQL server | `psql-stalltrack-clint-2026` |
| Production database | `stalltrack` |

> **Important:** This document intentionally contains no passwords, PayMongo keys, connection strings, or webhook secrets. Keep those only in Azure App Service application settings or another approved secret store.

---

## 1. Understand what is being deployed

The production system has two containerized web applications:

1. **API** (`EEMOCantilanSDS.Api`) — authentication, business rules, PostgreSQL access, PayMongo operations, SignalR, webhooks, and REST endpoints.
2. **Portal** (`EEMOCantilanSDS.Client`) — the staff/admin and payor web interface at `eemo.stalltrack.site`.

The MAUI mobile application is **not** deployed through Azure Container Registry. It is rebuilt and reinstalled separately when its code or API URL changes.

### Release order

Deploy the API first, verify it is healthy, then deploy the portal. The portal can safely be deployed second because it depends on the API.

```text
Development code
        |
        v
Tests + Git commit
        |
        v
Azure Container Registry builds two tagged images
        |
        +--> API App Service --> API health check
        |
        +--> Portal App Service --> browser verification
        |
        v
Targeted feature test in production
```

---

## 2. Before every release

Open PowerShell in the repository root:

```powershell
cd C:\dev\EEMOCantilanSDS
```

### 2.1 Check your Git state

```powershell
git status
git log -1 --oneline
```

Do not accidentally include unrelated work. If you see files you did not intend to release, stage only the files that belong to the release.

Example: stage a single documentation file only.

```powershell
git add docs\Production-Deployment-Runbook.md
```

### 2.2 Build and test locally

Run the smallest relevant test suite first. For API/application/payment work, use:

```powershell
dotnet test .\EEMOCantilanSDS.Testing\EEMOCantilanSDS.UnitTest.csproj --no-restore -v minimal
```

For a wider check, build the API and portal:

```powershell
dotnet build .\EEMOCantilanSDS.Api\EEMOCantilanSDS.Api.csproj -v minimal
dotnet build .\EEMOCantilanSDS.Client\EEMOCantilanSDS.Client.csproj -v minimal
```

Do not deploy a red test run just to “see if Azure fixes it.” Azure only runs the same compiled code somewhere harder to debug.

### 2.3 Commit and push the release code

Use a clear message that says what changed:

```powershell
git add <only-the-files-for-this-release>
git commit -m "feat: describe the production change"
git push origin master
```

Get the short commit ID. It will become the immutable image tag for this deployment:

```powershell
git rev-parse --short HEAD
```

Example output: `abc1234`. In the commands below, replace `abc1234` with the actual value returned by your Git command.

---

## 3. Confirm production configuration before an affected release

Most normal releases do not require changing Azure application settings. Check them whenever you are deploying authentication, domains, database configuration, PayMongo, CORS, or a new external integration.

In Azure Portal:

1. Open **App Services**.
2. Open `stalltrack-api-clint-2026`.
3. Open **Settings > Environment variables**.
4. Confirm values are present. Use **Show value** only when necessary, and never screenshot or paste secrets into chat.

### Required API production settings

| Setting | Expected production purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `OnlinePayments__PortalBaseUrl` | `https://eemo.stalltrack.site` |
| `Cors__AllowedOrigins__0` | `https://eemo.stalltrack.site` when required |
| `PayMongo__BaseUrl` | PayMongo API base URL for the selected test or live mode |
| `PayMongo__SecretKey` | Secret key for the selected PayMongo mode |
| `PayMongo__WebhookSecret` | Secret matching the configured PayMongo webhook |
| `ConnectionStrings__DefaultConnection` | Azure PostgreSQL connection string; do not expose it |

### Online payment production checklist

For a payment-related release, additionally confirm:

- `https://eemo.stalltrack.site` and `https://api.stalltrack.site` show **Secured** custom-domain bindings in Azure.
- The PayMongo webhook URL is exactly:

  ```text
  https://api.stalltrack.site/api/onlinepayments/webhook
  ```

- The webhook secret in PayMongo matches `PayMongo__WebhookSecret` in the API App Service.
- Test PayMongo keys, test webhook, and test payments are all used together. Do not mix test and live credentials.
- `OnlinePayments__PortalBaseUrl` is never `localhost`, an ngrok URL, or an expired development tunnel in Production.

> The API intentionally fails loudly in Production if the portal URL is missing or points to localhost. That is safer than sending a payer back to a dead address after payment.

---

## 4. Build tagged container images in Azure Container Registry

Azure Container Registry (ACR) builds the image in Azure. This is useful when Docker Desktop is not running locally.

Set the shared values once in PowerShell:

```powershell
$tag = git rev-parse --short HEAD
$registry = "acrstalltrackclint2026"
```

### 4.1 Build the API image

```powershell
az acr build `
  --registry $registry `
  --image "stalltrack-api:$tag" `
  --file EEMOCantilanSDS.Api/Dockerfile `
  .
```

### 4.2 Build the portal image

```powershell
az acr build `
  --registry $registry `
  --image "stalltrack-client:$tag" `
  --file EEMOCantilanSDS.Client/Dockerfile `
  .
```

### 4.3 Confirm both builds succeeded

```powershell
az acr task list --registry $registry --query "[].{Name:name,Status:provisioningState}" -o table

az acr repository show-tags --name $registry --repository stalltrack-api --orderby time_desc -o table
az acr repository show-tags --name $registry --repository stalltrack-client --orderby time_desc -o table
```

Wait for the new `$tag` to appear for both repositories before switching App Services to it.

### If an ACR build fails because Visual Studio files are locked

Close Visual Studio if possible, then check the root `.dockerignore`. It must exclude the repository-level Visual Studio folder:

```text
.vs
**/.vs
```

Do not remove `.dockerignore` entries just to make a build pass. They prevent local build artifacts, secrets, and locked development files from entering the container build context.

---

## 5. Deploy the API image first

Set the immutable image tag on the API App Service, then restart it:

```powershell
$tag = git rev-parse --short HEAD
$acrLoginServer = "acrstalltrackclint2026.azurecr.io"

az webapp config container set `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-api-clint-2026 `
  --container-image-name "$acrLoginServer/stalltrack-api:$tag"

az webapp restart `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-api-clint-2026
```

### 5.1 Verify API health

Container startup can take a minute. Retry the health endpoint until it returns HTTP 200:

```powershell
Invoke-WebRequest https://api.stalltrack.site/health -UseBasicParsing
```

Expected result: status code `200` and a small healthy JSON response such as `{"status":"ok"}`.

If the API does not become healthy:

1. Open `stalltrack-api-clint-2026` in Azure Portal.
2. Open **Log stream**.
3. Check for a startup exception, missing environment variable, database connection failure, or image-pull problem.
4. Do not deploy the portal until the API health endpoint succeeds.

---

## 6. Deploy the portal image second

After API health is confirmed, deploy the portal image with the same tag:

```powershell
az webapp config container set `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-web-clint-2026 `
  --container-image-name "$acrLoginServer/stalltrack-client:$tag"

az webapp restart `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-web-clint-2026
```

Verify it in a browser:

```text
https://eemo.stalltrack.site/login
```

The sign-in page should load without a 5xx error. Then sign in and exercise the one changed feature.

### Confirm what image an App Service is using

```powershell
az webapp config container show `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-api-clint-2026

az webapp config container show `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-web-clint-2026
```

Look for the expected `DOCKER|acrstalltrackclint2026.azurecr.io/...:<tag>` value.

---

## 7. Production verification by change type

### 7.1 Normal API/portal feature

1. Sign in with the intended role.
2. Complete the changed workflow once.
3. Refresh the affected page.
4. Confirm the database-backed result is still present after refresh.
5. Watch API Log Stream only if there is a problem.

### 7.2 PayMongo online payment release

Use PayMongo test mode unless the release is specifically approved for live money.

1. Start with a known unpaid test billing period.
2. Press **Pay** in the payor portal.
3. Complete the approved PayMongo test GCash checkout.
4. Confirm PayMongo says **PAID**.
5. Let the browser return completely to the EEMO **Payment received** page. Do not close it early; this runs the reconciliation fallback.
6. Refresh the payor portal and the staff facility/dashboard page.
7. Confirm the period is paid, the outstanding amount changed, and the staff total reflects the collection.
8. Preserve the PayMongo payment ID and checkout-session ID for the test record; never preserve the secret key.

The settlement logic is idempotent: webhook delivery and the return-page confirmation can both occur, but the same payment must not create a second EEMO payment record.

### 7.3 Mobile application release

The mobile code is independent from API/portal container deployment.

If the mobile app must use production API, its API base URL must be:

```text
https://api.stalltrack.site/
```

After changing mobile source:

1. Test or build the MAUI project locally.
2. Create the appropriate Android package/build.
3. Install the new build on the device.
4. Sign out/sign in if the old session or cached endpoint is still active.
5. Test a safe mobile workflow against production.

Do not silently mix a phone using an old ngrok/dev-tunnel URL with portal/API containers running in production.

---

## 8. Read logs and inspect data safely

### Azure API Log Stream

Use **App Service > stalltrack-api-clint-2026 > Log stream** to diagnose API-side requests.

For online payments, look for requests involving:

```text
/api/onlinepayments/initiate
/api/onlinepayments/confirm
/api/onlinepayments/webhook
```

Useful information to keep in a support note:

- timestamp and HTTP status code;
- non-secret error message;
- EEMO transaction reference;
- PayMongo payment ID or checkout-session ID.

Never copy authorization headers, connection strings, PayMongo secret keys, webhook secrets, or customer passwords.

### Azure PostgreSQL data inspection

Use pgAdmin or Azure Query Tool with SSL required. Review the production `stalltrack` database, then inspect the relevant EEMO payment tables. For online payment support, compare the transaction record with its bill/payment record before changing anything.

> A screenshot of PayMongo saying “payment successful” is not, by itself, authorization to manually mark a local bill paid. Verify the PayMongo ID and matching EEMO transaction first.

---

## 9. Safe rollback

Every ACR tag is an immutable release artifact. If a new release causes a production issue, revert to the previous known-good tag rather than rebuilding old source under a new ambiguous tag.

1. Identify prior tags:

   ```powershell
   az acr repository show-tags --name acrstalltrackclint2026 --repository stalltrack-api --orderby time_desc -o table
   az acr repository show-tags --name acrstalltrackclint2026 --repository stalltrack-client --orderby time_desc -o table
   ```

2. Choose the last known-good tag, for example `previousTag`.
3. Apply it to the API, restart, and verify `/health`.
4. Apply the matching portal tag, restart, and verify the login page.
5. Record the incident, affected release tag, rollback tag, and API log evidence before trying another fix.

Example API rollback:

```powershell
$previousTag = "previousTag"
$acrLoginServer = "acrstalltrackclint2026.azurecr.io"

az webapp config container set `
  --resource-group rg-stalltrack-prod `
  --name stalltrack-api-clint-2026 `
  --container-image-name "$acrLoginServer/stalltrack-api:$previousTag"

az webapp restart --resource-group rg-stalltrack-prod --name stalltrack-api-clint-2026
```

Repeat the same pattern for `stalltrack-web-clint-2026` with `stalltrack-client:$previousTag`.

---

## 10. Final release checklist

Before calling a release complete, verify all of these:

- [ ] Relevant local tests passed.
- [ ] Release code is committed and pushed.
- [ ] API image and portal image exist in ACR under the same Git-based tag.
- [ ] API was deployed first and `https://api.stalltrack.site/health` returned 200.
- [ ] Portal was deployed second and `https://eemo.stalltrack.site/login` loads.
- [ ] The changed feature was tested in production with a real authorized account.
- [ ] For payment work: PayMongo result, EEMO database settlement, and both portal views were verified.
- [ ] No secrets were committed, pasted into logs, or included in screenshots/support notes.
- [ ] Any mobile-app change was packaged and installed separately.

When the checklist is complete, record the deployed Git/image tag and the date in the release note or issue tracker. That one habit makes future troubleshooting dramatically easier.
