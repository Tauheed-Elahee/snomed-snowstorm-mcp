# snomed-snowstorm-mcp
MCP server for SNOMED Snowstorm (GitHub repo: https://github.com/IHTSDO/snowstorm) that uses SNOMED terminalogy

MCP Server is written in C# in .NET 10 and is designed to be deployed to Azure.

Set the `SNOWSTORM_URL` app setting (or environment variable) to the base URL of your Snowstorm instance, e.g. `https://snowstorm.example.org`. If unset, it defaults to the placeholder `https://snowstorm.snomed.example.org`. For local development, add it to `local.settings.json` under `Values`.
