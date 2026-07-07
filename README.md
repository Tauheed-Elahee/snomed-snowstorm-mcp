# snomed-snowstorm-mcp
MCP server for SNOMED Snowstorm (GitHub repo: https://github.com/IHTSDO/snowstorm) that uses SNOMED terminalogy

MCP Server is written in C# in .NET 10 and is designed to be deployed to Azure.

Set the `SNOWSTORM_URL` app setting (or environment variable) to the base URL of your Snowstorm instance, e.g. `https://snowstorm.example.org`. If unset, it defaults to the placeholder `https://snowstorm.snomed.example.org`. For local development, add it to `local.settings.json` under `Values`.

## Tools

- `search_concepts` — search by clinical term (3-250 chars), optionally filtered by semantic tag (e.g. disorder, finding, procedure)
- `get_concept` — full details for one concept: FSN, preferred term, synonyms, active and definition status
- `get_parents` / `get_children` — direct IS-A neighbours of a concept
- `get_ancestors` — all transitive ancestors of a concept
- `validate_concept` — check a concept ID exists and is active
- `ecl_query` — arbitrary Expression Constraint Language queries
- `get_terminology_info` — edition metadata and concept counts
