---
title: Error Catalogue
nav_order: 4
---

# Error Catalogue

Every failure returns a structured JSON object rather than a thrown exception, because the MCP
SDK masks exceptions into a generic *"An error occurred invoking '&lt;tool&gt;'"* that gives the
calling agent nothing to act on. Each message below is written so an agent can self-correct and
retry within the same run.

All error cases were verified against a live Snowstorm 10.9.1 instance.

## Input validation (checked before calling Snowstorm)

| Trigger | Response |
|---|---|
| `term` missing, under 3, or over 250 characters | `{"error":"Search term must be 3 to 250 characters; use a single short clinical term (a few words), not a sentence."}` |
| `concept_id` not a 6–18 digit number (e.g. `heart attack`, `999`) | `{"error":"concept_id must be a SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006. Received: \"heart attack\"."}` |
| malformed `concept_id` in `validate_concept` | `{"valid":false,"reason":"concept_id is not a well-formed SNOMED CT identifier (6-18 digit number)."}` |
| `ecl` empty | `{"error":"ecl must be a non-empty SNOMED CT Expression Constraint Language expression, e.g. \"<< 73211009\"."}` |
| `limit` outside 1–10000 | `{"error":"limit must be between 1 and 10000."}` |
| unknown `semantic_tag` | `{"error":"Unknown semantic tag 'super-disease'. Known tags: disorder, finding, procedure, …"}` |

Two of these guards close **silent-garbage** paths, not error paths: Snowstorm answers an empty
`term` or empty `ecl` with HTTP 200 and an arbitrary page of concepts (including inactive ones)
— results an agent would trust. The guards make those a hard error instead.

## Snowstorm errors (propagated with Snowstorm's own message)

| Trigger | Response |
|---|---|
| well-formed but nonexistent/inactive ID in `get_parents` / `get_children` / `get_ancestors` | `{"error":"Concepts in the ECL request do not exist or are inactive on branch MAIN: 961000000000000108."}` |
| nonexistent ID in `get_concept` | `{"error":"Concept 961000000000000108 not found."}` |
| nonexistent ID in `validate_concept` | `{"valid":false}` |
| ECL syntax error | `{"error":"No viable alternative at line 1, character 0."}` (Snowstorm's parser message) |
| any other non-2xx from Snowstorm | `{"error":"<Snowstorm's message>"}` or `{"error":"Snowstorm returned HTTP <status>."}` |

## Infrastructure errors

| Trigger | Response |
|---|---|
| Snowstorm unreachable (down, DNS, firewall) or the request timed out | `{"error":"Could not reach the Snowstorm terminology server: <details>"}` |
| HTTP 200 with a non-JSON body (typically a misrouted proxy serving an HTML page because `SNOWSTORM_URL` points at the wrong path) | `{"error":"Snowstorm returned a non-JSON response; check that SNOWSTORM_URL points at the Snowstorm API base URL."}` |

## Guidance for agent instructions

If you write system instructions for an agent using these tools, two lines prevent most
failures observed in practice:

> Call `search_concepts` with one short clinical term at a time (a few words), never a sentence.
> Concept IDs are 6–18 digit numbers; get them from search results, never invent them.
