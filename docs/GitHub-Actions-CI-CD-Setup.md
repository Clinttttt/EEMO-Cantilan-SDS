# GitHub Actions CI/CD Setup

This guide connects the StallTrack repository to Azure so GitHub Actions can verify every change and deploy approved `master` commits to Production.

## What the workflows do

| Workflow | Trigger | What it does |
|---|---|---|
| `CI` | Pull request, push to `master`, or manual run | Restores, builds the API and portal, runs the unit tests, and uploads test results. It never changes Azure. |
| `Deploy production` | Push to `master` or manual run | Re-runs tests, builds immutable API and portal images tagged with the full Git commit SHA, pushes them to ACR, deploys API first, checks `/health`, then deploys the portal. |

Production resources used by the deployment workflow:

| Resource | Value |
|---|---|
| Resource group | `rg-stalltrack-prod` |
| Container registry | `acrstalltrackclint2026.azurecr.io` |
| API App Service | `stalltrack-api-clint-2026` |
| Portal App Service | `stalltrack-web-clint-2026` |
| API health endpoint | `https://api.stalltrack.site/health` |
| Portal check | `https://eemo.stalltrack.site/login` |

No database password, PayMongo key, or application setting is stored in these GitHub workflow files. Those remain in Azure App Service configuration.

---

## One-time Azure setup: create the GitHub deployment identity

GitHub Actions should authenticate to Azure through **OpenID Connect (OIDC)**. This avoids keeping an Azure password or client secret in GitHub.

Run these commands in **Azure Cloud Shell (Bash)** while signed in to the subscription that owns `rg-stalltrack-prod`.

Replace `OWNER/REPOSITORY` with the repository shown by `git remote -v`. For this project it is currently `Clinttttt/EEMO-Cantilan-SDS`.

```bash
SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
TENANT_ID="$(az account show --query tenantId --output tsv)"
RG="rg-stalltrack-prod"
ACR_NAME="acrstalltrackclint2026"
APP_NAME="github-stalltrack-production"
REPOSITORY="Clinttttt/EEMO-Cantilan-SDS"

APP_ID="$(az ad app create --display-name "$APP_NAME" --query appId --output tsv)"
az ad sp create --id "$APP_ID"
```

Create a federated credential that permits the `production` GitHub Environment to request an Azure token:

```bash
cat > credential.json <<EOF
{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:${REPOSITORY}:environment:production",
  "description": "GitHub Actions deployment from the production environment",
  "audiences": ["api://AzureADTokenExchange"]
}
EOF

az ad app federated-credential create --id "$APP_ID" --parameters credential.json
```

Give that identity only the two permissions it needs:

```bash
ACR_ID="$(az acr show --name "$ACR_NAME" --resource-group "$RG" --query id --output tsv)"
RG_ID="$(az group show --name "$RG" --query id --output tsv)"

az role assignment create --assignee "$APP_ID" --role "AcrPush" --scope "$ACR_ID"
az role assignment create --assignee "$APP_ID" --role "Website Contributor" --scope "$RG_ID"
```

Keep these three values; you will enter them as GitHub variables:

```bash
echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
```

> If Azure says you are not authorized to create the app registration or role assignment, ask the subscription/Entra administrator to run these one-time commands. Do **not** use a personal access token or a long-lived Azure password as a workaround.

---

## One-time GitHub setup

1. Open the repository on GitHub: `Settings` > `Environments` > `New environment`.
2. Name it exactly **`production`** (lowercase). The workflow expects this exact name.
3. If your GitHub plan supports it, enable **Required reviewers** and add yourself. This pauses production deployment until you approve it in GitHub Actions.
4. In the same `production` environment, open **Environment variables** and add:

   | Name | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | The `APP_ID` printed by Azure Cloud Shell |
   | `AZURE_TENANT_ID` | Your Azure tenant ID |
   | `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID |

5. Commit and push the files in `.github/workflows/`.
6. Open GitHub > `Actions`. You should see **CI** and **Deploy production** in the left sidebar.

### Recommended branch rule

On GitHub, open `Settings` > `Branches` > `Add branch protection rule` for `master`.

- Require a pull request before merging.
- Require the **CI / Build and test** check to pass.
- Require your approval for the `production` environment if it is available on your plan.

This prevents a broken build from reaching Azure.

---

## Normal release process after setup

1. Create or use a feature branch.
2. Make the change locally and run the relevant tests.
3. Push the feature branch and open a pull request to `master`.
4. Wait for the **CI** check to pass.
5. Merge the pull request into `master`.
6. GitHub starts **Deploy production**. Approve the `production` environment when prompted.
7. The workflow deploys the API, waits for `https://api.stalltrack.site/health`, then deploys the portal.
8. Open `https://eemo.stalltrack.site/login` and test the changed workflow.

The deployed image tag is the full Git commit SHA. This makes it possible to identify exactly which source revision is running in production.

---

## First dry run

Before merging a real feature:

1. Commit the workflow files to a branch.
2. Open a pull request into `master` and confirm **CI** passes.
3. Merge only when the Azure OIDC variables and permissions are configured.
4. Watch the first **Deploy production** run in GitHub Actions.
5. If the deployment pauses at `production`, review it and select **Approve and deploy**.

If the first run fails at Azure login, re-check the `production` environment name, the three variables, and the federated credential `subject` value. They must all agree exactly.

---

## Safe rollback

CI/CD does not delete prior ACR image tags. If a release has a problem:

1. In Azure Cloud Shell, list prior tags:

   ```bash
   az acr repository show-tags --name acrstalltrackclint2026 --repository stalltrack-api --orderby time_desc --output table
   az acr repository show-tags --name acrstalltrackclint2026 --repository stalltrack-client --orderby time_desc --output table
   ```

2. Follow the rollback section in [Production Deployment Runbook](Production-Deployment-Runbook.md) using the last known-good tag.
3. Investigate and fix the source change in a new pull request. Do not force-push or rewrite production history.
