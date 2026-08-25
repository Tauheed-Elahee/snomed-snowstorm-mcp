---
title: Setup & Deployment
nav_order: 3
---

# Setup & Deployment

## Prerequisites

- A running [Snowstorm](https://github.com/IHTSDO/snowstorm) instance with a SNOMED CT edition
  loaded, reachable from Azure.
- Azure subscription + [Azure CLI](https://learn.microsoft.com/cli/azure/).
- .NET 10 SDK for local builds.

## 1. Create the Function App

Use the **Flex Consumption** plan. Classic Linux Consumption cannot actually run
`DOTNET-ISOLATED|10.0` (the host never starts, 503s on both the app and SCM site even though the
stack is listed as GA) and the plan reaches end of life on 2028-09-30.

```bash
az functionapp create \
  -g <resource-group> -n <app-name> \
  --flexconsumption-location <region> \
  --runtime dotnet-isolated --runtime-version 10 \
  --storage-account <storage-account>
```

## 2. Configure the Snowstorm URL

The app reads its Snowstorm base URL from the `SNOWSTORM_URL` setting (falling back to a
placeholder if unset):

```bash
az functionapp config appsettings set -n <app-name> -g <resource-group> \
  --settings SNOWSTORM_URL=https://snowstorm.snomed.example.org
```

If Snowstorm sits behind a path prefix (a common nginx layout serves the API at
`/snowstorm/snomed-ct`), include it: `https://snomed.example.org/snowstorm/snomed-ct`.

## 3. Build and deploy

Merging to `main` deploys (`.github/workflows/deploy.yml`): the build is
stamped with the commit (`-p:SourceRevisionId`), pushed with the Azure
Functions action under a GitHub→Azure OIDC identity (repository variables
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; environment
`deploy`), and the run ends by asking the server which commit it attests.

By hand, as a fallback:

```bash
dotnet publish -c Release -o publish
cd publish && zip -r ../deploy.zip . && cd ..
az functionapp deployment source config-zip -n <app-name> -g <resource-group> --src deploy.zip
```

A hand deploy carries no commit; `Public/Terminology` then reports `Commit: null`.

## 3a. What the server says about itself

```
GET https://<app-name>.azurewebsites.net/api/Public/Terminology
```

Anonymous, open-CORS, deployment facts only: the loaded edition
(`Edition`, `Version`, `ImportDate` — Snowstorm's `/codesystems`, no count
queries), `ServerVersion` and `Commit` (the build's), `GeneratedAtUtc`.
Cached for a minute. The Consultologist engine reads it once per process
and stamps the edition and the server build on every job record.

## 4. Connect an MCP client

The MCP endpoint (streamable HTTP) is:

```
https://<app-name>.azurewebsites.net/runtime/webhooks/mcp
```

Authentication uses the `mcp_extension` system key:

```bash
az functionapp keys list -n <app-name> -g <resource-group> --query "systemKeys.mcp_extension" -o tsv
```

**Claude Code** (`~/.claude.json` or `.mcp.json`) — use the `http` transport, passing the key as
the `code` query parameter:

```json
{
  "mcpServers": {
    "snomed": {
      "type": "http",
      "url": "https://<app-name>.azurewebsites.net/runtime/webhooks/mcp?code=<mcp_extension-key>"
    }
  }
}
```

**Azure AI Foundry** — configure the MCP tool with the bare URL and send the key as a header:
`x-functions-key: <mcp_extension-key>`.

The key is per-app: redeploying to a *new* Function App rotates it, and every client must be
updated.

## Local development

Add the URL to `local.settings.json` (gitignored):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "SNOWSTORM_URL": "https://snowstorm.snomed.example.org"
  }
}
```

Then `func start` (Azure Functions Core Tools) runs the MCP server locally.
