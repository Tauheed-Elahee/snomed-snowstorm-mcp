# Candidate tools — on the shelf, not scheduled

Assessment from 2026-07-07, after the toolset revision that landed `get_concept`, `get_parents`,
tag-filtered `search_concepts`, and removed `get_by_semantic_tag` (commits `88f4e09`, `c60e2a4`).

**Position: the current 8-tool set is well-shaped — add nothing until a concrete need shows up
in real agent traces.** Every tool earns its place and each addition costs agent attention
(more schema tokens per call, more choices to get wrong). No subtractions recommended.

Watch how the Foundry agent actually uses the toolset before building any of these:
Application Insights records every tool invocation with arguments (the `_logger.LogInformation`
call at the top of each function).

## 1. `validate_concepts` (batch) — build first if needed

- **Why**: consult generation extracts multiple concepts per document; each validation is
  currently a separate tool call, and in a Foundry agent loop every tool call costs a full
  model turn. Batching N validations into one call is a real latency and token win.
- **How**: `concept_ids` as a comma-separated list; Snowstorm accepts
  `GET /MAIN/concepts?conceptIds=X,Y,Z`. Validate each ID with `IsValidSctId` (report
  malformed ones individually), return one entry per requested ID.
- **Trigger to build**: App Insights shows repeated single-concept `validate_concept` bursts
  within one consult run.

## 2. `map_to_icd10(concept_id)`

- **Why**: consult letters / downstream billing may need ICD-10 codes alongside SNOMED.
- **How**: ICD-10 map reference set `447562003` via
  `GET /MAIN/members?referenceSet=447562003&referencedComponentId={id}&active=true`;
  return `mapTarget` (+ `mapAdvice`). Verified working on the live instance 2026-07-07:
  22298006 (myocardial infarction) → `I21.9`.
- **Caveat**: this is the **WHO international ICD-10** map. Canadian billing typically uses
  **ICD-10-CA** (CIHI), which is licensed separately and NOT in this edition. Confirm WHO
  ICD-10 is actually what the workflow needs before building.

## 3. `is_descendant_of(concept_id, ancestor_id)`

- **Why**: yes/no subsumption check ("is this a kind of diabetes?"). Expressible today as
  `ecl_query("{id} AND << {ancestor}")`, but agents are unreliable ECL authors; a dedicated
  boolean tool removes that failure mode.
- **How**: run that ECL with limit=1; return `{subsumed: true|false}`. Reuse `IsValidSctId`
  guards for both parameters.
- **Priority**: cheapest to build, lowest urgency.
