---
title: Home
nav_order: 1
---

# SNOMED Snowstorm MCP

An [MCP](https://modelcontextprotocol.io) server that gives AI agents safe, validated access to
[SNOMED CT](https://www.snomed.org) terminology. It is a thin proxy over a
[Snowstorm](https://github.com/IHTSDO/snowstorm) terminology server, written in C# on .NET 10 and
designed to run as an Azure Functions app.

```
MCP client (Claude, Azure AI Foundry, …)
        │  streamable HTTP  /runtime/webhooks/mcp
        ▼
Azure Functions app (this repo)
        │  REST
        ▼
Snowstorm terminology server  ──  SNOMED CT edition
```

Every tool validates its inputs against Snowstorm's real constraints and returns structured,
self-correctable errors (`{"error": "…"}`) instead of masked exceptions — so an agent that
passes a sentence instead of a search term, or a malformed concept ID, learns exactly what to
fix and can retry within the same run.

## Quickstart

1. **Deploy** the app to Azure Functions (.NET 10 isolated, Functions v4). The Flex Consumption
   plan is recommended — see [Setup & Deployment]({% link setup.md %}).
2. **Point it at Snowstorm** with one app setting:

   ```
   SNOWSTORM_URL=https://snowstorm.snomed.example.org
   ```

3. **Connect your MCP client** to `https://<your-app>.azurewebsites.net/runtime/webhooks/mcp`
   using the `mcp_extension` system key.

## The tools

| Tool | Purpose |
|---|---|
| `search_concepts` | Search concepts by clinical term, optionally filtered by semantic tag |
| `get_concept` | Full details for one concept: FSN, preferred term, synonyms, status |
| `get_parents` / `get_children` | Direct IS-A neighbours |
| `get_ancestors` | All transitive ancestors |
| `validate_concept` | Check a concept ID exists and is active |
| `ecl_query` | Arbitrary Expression Constraint Language queries |
| `get_terminology_info` | Edition metadata and concept counts |

See the full [Tools Reference]({% link tools.md %}) and the [Error Catalogue]({% link errors.md %}).
