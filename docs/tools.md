---
title: Tools Reference
nav_order: 2
---

# Tools Reference

All tools return JSON strings. Failures are always a structured object — `{"error": "message"}` —
with a message written so a calling agent can correct its input and retry. Success shapes are
listed per tool. Source of truth: [`SnowstormFunctions.cs`](https://github.com/Tauheed-Elahee/snomed-snowstorm-mcp/blob/main/SnowstormFunctions.cs).

Concept IDs (SCTIDs) are **6–18 digit numbers**, e.g. `22298006`. Limits are **1–10000**.

---

## search_concepts

Search SNOMED CT concepts by term, optionally filtered by semantic tag.

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `term` | string | yes | 3–250 characters; a single short clinical concept, not a sentence |
| `semantic_tag` | string | no | one of: disorder, finding, procedure, body structure, substance, organism, qualifier value, observable entity, product, situation, event, record artifact, specimen, social concept, morphologic abnormality, attribute, occupation, environment, physical force, physical object, cell, regime/therapy |
| `limit` | number | no | 1–10000, default 10 |

**Returns** an array of `{id, fsn, pt}`:

```json
[{"id":"195967001","fsn":"Asthma (disorder)","pt":"Asthma"}]
```

The `semantic_tag` filter disambiguates overloaded words — searching `cold` with
`semantic_tag=disorder` returns *Common cold* and *Cold burn* rather than temperature qualifiers.

## get_concept

Full details for one concept.

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `concept_id` | string | yes | 6–18 digit SCTID |

**Returns**:

```json
{
  "id": "22298006",
  "fsn": "Myocardial infarction (disorder)",
  "pt": "Myocardial infarction",
  "active": true,
  "definitionStatus": "FULLY_DEFINED",
  "synonyms": ["Cardiac infarction", "Heart attack", "MI - myocardial infarction", "..."]
}
```

Synonyms are active English synonyms — useful for choosing patient-friendly wording.

## get_parents / get_children

Direct IS-A neighbours of a concept (one hierarchy level).

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `concept_id` | string | yes | 6–18 digit SCTID |

**Returns** an array of `{id, fsn, pt}`. For `22298006` (myocardial infarction), `get_parents`
returns exactly *Ischemic heart disease* and *Myocardial necrosis*.

## get_ancestors

All transitive ancestors via the IS-A hierarchy (ECL `>>`). Prefer `get_parents` when you only
need the immediate level — ancestor sets are large (32 concepts for myocardial infarction).

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `concept_id` | string | yes | 6–18 digit SCTID |

**Returns** an array of `{id, fsn, pt}`.

## validate_concept

Check whether a concept ID exists and is active.

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `concept_id` | string | yes | 6–18 digit SCTID |

**Returns** one of:

```json
{"valid": true, "active": true, "fsn": "Myocardial infarction (disorder)"}
{"valid": false}
{"valid": false, "reason": "concept_id is not a well-formed SNOMED CT identifier (6-18 digit number)."}
```

The `reason` variant distinguishes a malformed ID from a well-formed ID that simply doesn't exist.

## ecl_query

Run an arbitrary [Expression Constraint Language](https://confluence.ihtsdotools.org/display/DOCECL)
query.

| Parameter | Type | Required | Constraints |
|---|---|---|---|
| `ecl` | string | yes | non-empty ECL expression, e.g. `<< 73211009` |
| `limit` | number | no | 1–10000, default 20 |

**Returns** an array of `{id, fsn, pt}`. Syntax errors surface Snowstorm's own parser message.

## get_terminology_info

Summary statistics for the loaded SNOMED CT edition. No parameters.

**Returns**:

```json
{
  "edition": "SNOMEDCT 20251130 import.",
  "version": "2025-11-30",
  "import_date": "2025-12-21T22:39:16.944Z",
  "active_concepts": 410945,
  "total_concepts": 562353,
  "descriptions": 2217069,
  "semantic_tags": {"disorder": 91569, "finding": 128227, "...": 0}
}
```

Note: this tool fans out ~25 parallel count queries — it is intended for diagnostics, not
per-request use.
