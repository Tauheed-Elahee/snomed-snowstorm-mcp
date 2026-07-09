using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConsultologistSnomedMcp;

public class SnowstormFunctions
{
    private static readonly string SnowstormRoot =
        (Environment.GetEnvironmentVariable("SNOWSTORM_URL") ?? "https://snowstorm.snomed.example.org").TrimEnd('/');
    private static readonly string SnowstormBase = $"{SnowstormRoot}/MAIN";

    private readonly HttpClient _http;
    private readonly ILogger<SnowstormFunctions> _logger;

    public SnowstormFunctions(IHttpClientFactory httpClientFactory, ILogger<SnowstormFunctions> logger)
    {
        _http = httpClientFactory.CreateClient();
        _logger = logger;
    }

    // ── search_concepts ──────────────────────────────────────────────────────

    [Function(nameof(SearchConcepts))]
    public async Task<string> SearchConcepts(
        [McpToolTrigger("search_concepts", "Search SNOMED CT concepts by term, optionally filtered by semantic tag. Returns a JSON array of {id, fsn, pt}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("term",         "string", true,  Description = "Clinical search term, 3-250 characters. A single short clinical concept (a few words), not a sentence.")] string term,
        [McpToolProperty("semantic_tag", "string", false, Description = "Optional semantic tag filter, e.g. disorder, finding, procedure, body structure, substance.")]            string? semanticTag,
        [McpToolProperty("limit",        "number", false, Description = "Maximum results to return, 1-10000 (default 10).")]                                                       int? limit)
    {
        _logger.LogInformation("search_concepts: term={Term} tag={Tag} limit={Limit}", term, semanticTag, limit);
        // Snowstorm requires 3-250 characters; an empty term silently returns arbitrary concepts.
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length is < 3 or > 250)
            return JsonSerializer.Serialize(new { error = "Search term must be 3 to 250 characters; use a single short clinical term (a few words), not a sentence." });
        if (LimitError(limit) is { } limitError)
            return limitError;
        var url = $"{SnowstormBase}/concepts?term={Uri.EscapeDataString(term.Trim())}&active=true&limit={limit ?? 10}";
        if (!string.IsNullOrWhiteSpace(semanticTag))
        {
            if (!SemanticTagRoots.TryGetValue(semanticTag.Trim(), out var rootId))
                return JsonSerializer.Serialize(new { error = $"Unknown semantic tag '{semanticTag}'. Known tags: {string.Join(", ", SemanticTagRoots.Keys)}" });
            url += $"&ecl={Uri.EscapeDataString($"<< {rootId}")}";
        }
        return await FetchConcepts(new Uri(url));
    }

    // ── get_ancestors ─────────────────────────────────────────────────────────

    [Function(nameof(GetAncestors))]
    public async Task<string> GetAncestors(
        [McpToolTrigger("get_ancestors", "Get all ancestors of a SNOMED concept via IS-A hierarchy (transitive; use get_parents for direct parents only). Returns a JSON array of {id, fsn, pt}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006.")] string conceptId)
    {
        _logger.LogInformation("get_ancestors: {Id}", conceptId);
        if (!IsValidSctId(conceptId))
            return SctIdError("concept_id", conceptId);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($">> {conceptId!.Trim()}")}&active=true&limit=50";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_children ──────────────────────────────────────────────────────────

    [Function(nameof(GetChildren))]
    public async Task<string> GetChildren(
        [McpToolTrigger("get_children", "Get direct children of a SNOMED concept. Returns a JSON array of {id, fsn, pt}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006.")] string conceptId)
    {
        _logger.LogInformation("get_children: {Id}", conceptId);
        if (!IsValidSctId(conceptId))
            return SctIdError("concept_id", conceptId);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($"<! {conceptId!.Trim()}")}&active=true&limit=50";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_parents ───────────────────────────────────────────────────────────

    [Function(nameof(GetParents))]
    public async Task<string> GetParents(
        [McpToolTrigger("get_parents", "Get direct parents of a SNOMED concept. Returns a JSON array of {id, fsn, pt}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006.")] string conceptId)
    {
        _logger.LogInformation("get_parents: {Id}", conceptId);
        if (!IsValidSctId(conceptId))
            return SctIdError("concept_id", conceptId);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($">! {conceptId!.Trim()}")}&active=true&limit=50";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_concept ───────────────────────────────────────────────────────────

    [Function(nameof(GetConcept))]
    public async Task<string> GetConcept(
        [McpToolTrigger("get_concept", "Get full details for one SNOMED CT concept. Returns {id, fsn, pt, active, definitionStatus, synonyms}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006.")] string conceptId)
    {
        _logger.LogInformation("get_concept: {Id}", conceptId);
        if (!IsValidSctId(conceptId))
            return SctIdError("concept_id", conceptId);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"{SnowstormRoot}/browser/MAIN/concepts/{conceptId.Trim()}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Snowstorm unreachable for get_concept: {Id}", conceptId);
            return JsonSerializer.Serialize(new { error = $"Could not reach the Snowstorm terminology server: {ex.Message}" });
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return JsonSerializer.Serialize(new { error = $"Concept {conceptId.Trim()} not found." });

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return SnowstormErrorJson(response.StatusCode, content);

        var body = TryParse(content);
        if (body is null)
            return NonJsonError;
        var synonyms = body?["descriptions"]?.AsArray()
            .Where(d => d?["active"]?.GetValue<bool>() == true
                     && d?["type"]?.ToString() == "SYNONYM"
                     && d?["lang"]?.ToString() == "en")
            .Select(d => d?["term"]?.ToString())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            id               = body?["conceptId"]?.ToString(),
            fsn              = body?["fsn"]?["term"]?.ToString(),
            pt               = body?["pt"]?["term"]?.ToString(),
            active           = body?["active"]?.GetValue<bool>(),
            definitionStatus = body?["definitionStatus"]?.ToString(),
            synonyms
        });
    }

    // ── validate_concept ──────────────────────────────────────────────────────

    [Function(nameof(ValidateConcept))]
    public async Task<string> ValidateConcept(
        [McpToolTrigger("validate_concept", "Check if a SNOMED concept ID is valid and active. Returns {valid, active, fsn}; {valid: false} if unknown, with a reason if malformed; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID to validate: a 6-18 digit number, e.g. 22298006.")] string conceptId)
    {
        _logger.LogInformation("validate_concept: {Id}", conceptId);
        if (!IsValidSctId(conceptId))
            return JsonSerializer.Serialize(new { valid = false, reason = "concept_id is not a well-formed SNOMED CT identifier (6-18 digit number)." });
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"{SnowstormBase}/concepts/{Uri.EscapeDataString(conceptId ?? "")}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Snowstorm unreachable for validate_concept: {Id}", conceptId);
            return JsonSerializer.Serialize(new { error = $"Could not reach the Snowstorm terminology server: {ex.Message}" });
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return """{"valid":false}""";

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return SnowstormErrorJson(response.StatusCode, content);

        var body = TryParse(content);
        if (body is null)
            return NonJsonError;
        return JsonSerializer.Serialize(new
        {
            valid  = true,
            active = body?["active"]?.GetValue<bool>(),
            fsn    = body?["fsn"]?["term"]?.ToString()
        });
    }

    // ── ecl_query ─────────────────────────────────────────────────────────────

    [Function(nameof(EclQuery))]
    public async Task<string> EclQuery(
        [McpToolTrigger("ecl_query", "Run an arbitrary ECL query against SNOMED CT. Returns a JSON array of {id, fsn, pt}; on failure returns {error: message}.")] ToolInvocationContext context,
        [McpToolProperty("ecl",   "string", true,  Description = "SNOMED CT Expression Constraint Language expression, e.g. \"<< 73211009\".")] string ecl,
        [McpToolProperty("limit", "number", false, Description = "Maximum results to return, 1-10000 (default 20).")]                           int? limit)
    {
        _logger.LogInformation("ecl_query: {Ecl}", ecl);
        // An empty ECL expression silently returns arbitrary concepts.
        if (string.IsNullOrWhiteSpace(ecl))
            return JsonSerializer.Serialize(new { error = "ecl must be a non-empty SNOMED CT Expression Constraint Language expression, e.g. \"<< 73211009\"." });
        if (LimitError(limit) is { } limitError)
            return limitError;
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString(ecl)}&active=true&limit={limit ?? 20}";
        return await FetchConcepts(new Uri(url));
    }

    // Semantic tag → SNOMED hierarchy root concept (ECL-based, reliable on this instance)
    private static readonly Dictionary<string, string> SemanticTagRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disorder"]              = "64572001",
        ["finding"]               = "404684003",
        ["procedure"]             = "71388002",
        ["body structure"]        = "123037004",
        ["substance"]             = "105590001",
        ["organism"]              = "410607006",
        ["qualifier value"]       = "362981000",
        ["observable entity"]     = "363787002",
        ["product"]               = "373873005",
        ["situation"]             = "243796009",
        ["event"]                 = "272379006",
        ["record artifact"]       = "419891008",
        ["specimen"]              = "123038009",
        ["social concept"]        = "48176007",
        ["morphologic abnormality"] = "49755003",
        ["attribute"]             = "246061005",
        ["occupation"]            = "14679004",
        ["environment"]           = "308916002",
        ["physical force"]        = "78621006",
        ["physical object"]       = "260787004",
        ["cell"]                  = "4421005",
        ["regime/therapy"]        = "243120004",
    };

    // ── get_terminology_info ──────────────────────────────────────────────────

    [Function(nameof(GetTerminologyInfo))]
    public async Task<string> GetTerminologyInfo(
        [McpToolTrigger("get_terminology_info", "Get summary statistics for the loaded SNOMED CT edition. Returns {edition, version, import_date, active_concepts, total_concepts, descriptions, semantic_tags}; on failure returns {error: message}.")] ToolInvocationContext context)
    {
        _logger.LogInformation("get_terminology_info");

        try
        {
            return await BuildTerminologyInfo();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Snowstorm request failed in get_terminology_info");
            return JsonSerializer.Serialize(new { error = $"Could not reach the Snowstorm terminology server: {ex.Message}" });
        }
    }

    private async Task<string> BuildTerminologyInfo()
    {
        // 1. Fetch edition metadata from /codesystems
        var codesysJson = await _http.GetStringAsync($"{SnowstormRoot}/codesystems");
        var codesys     = TryParse(codesysJson)
            ?? throw new HttpRequestException("Snowstorm returned a non-JSON response; check that SNOWSTORM_URL points at the Snowstorm API base URL.");
        var latest      = codesys?["items"]?[0]?["latestVersion"];

        // 2. Parallel count queries
        var activeConcTask = GetTotal($"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString("<< 138875005")}&active=true&limit=1");
        var totalConcTask  = GetTotal($"{SnowstormBase}/concepts?limit=1");
        var descTask       = GetTotal($"{SnowstormBase}/descriptions?active=true&limit=1");

        // 3. Semantic tag counts — all 22 tags, fully parallel
        var tagTasks = SemanticTagRoots.ToDictionary(
            kv => kv.Key,
            kv => GetTotal($"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($"<< {kv.Value}")}&active=true&limit=1"));

        await Task.WhenAll(new[] { activeConcTask, totalConcTask, descTask }
            .Concat(tagTasks.Values));

        var tagCounts = tagTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result);

        return JsonSerializer.Serialize(new
        {
            edition         = latest?["description"]?.ToString(),
            version         = latest?["version"]?.ToString(),
            import_date     = latest?["importDate"]?.ToString(),
            active_concepts = activeConcTask.Result,
            total_concepts  = totalConcTask.Result,
            descriptions    = descTask.Result,
            semantic_tags   = tagCounts
        });
    }

    // ── shared helpers ────────────────────────────────────────────────────────

    private async Task<long> GetTotal(string url)
    {
        var json = await _http.GetStringAsync(url);
        var data = TryParse(json)
            ?? throw new HttpRequestException("Snowstorm returned a non-JSON response; check that SNOWSTORM_URL points at the Snowstorm API base URL.");
        return data["total"]?.GetValue<long>() ?? 0;
    }

    private async Task<string> FetchConcepts(Uri uri)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(uri);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Snowstorm unreachable: {Uri}", uri);
            return JsonSerializer.Serialize(new { error = $"Could not reach the Snowstorm terminology server: {ex.Message}" });
        }

        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Snowstorm returned {Status} for {Uri}: {Body}", (int)response.StatusCode, uri, json);
            return SnowstormErrorJson(response.StatusCode, json);
        }

        var data = TryParse(json);
        if (data is null)
            return NonJsonError;
        var items = data?["items"]?.AsArray()
            .Select(c => new
            {
                id  = c?["conceptId"]?.ToString(),
                fsn = c?["fsn"]?["term"]?.ToString(),
                pt  = c?["pt"]?["term"]?.ToString()
            })
            .ToArray();
        return JsonSerializer.Serialize(items);
    }

    // A 200 response can still carry non-JSON (e.g. an HTML page from a misrouted proxy
    // when SNOWSTORM_URL points at the wrong path) — treat that as an error, not a crash.
    private static JsonNode? TryParse(string body)
    {
        try { return JsonNode.Parse(body); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static readonly string NonJsonError = JsonSerializer.Serialize(new
        { error = "Snowstorm returned a non-JSON response; check that SNOWSTORM_URL points at the Snowstorm API base URL." });

    // SCTIDs are 6-18 digit numbers.
    private static bool IsValidSctId(string? id) =>
        id is not null && id.Trim().Length is >= 6 and <= 18 && id.Trim().All(char.IsAsciiDigit);

    private static string SctIdError(string paramName, string? value) =>
        JsonSerializer.Serialize(new { error = $"{paramName} must be a SNOMED CT concept ID: a 6-18 digit number, e.g. 22298006. Received: \"{value}\"." });

    // Snowstorm rejects limits outside 1-10000 (unsorted offset + page size cap).
    private static string? LimitError(int? limit) =>
        limit is < 1 or > 10000
            ? JsonSerializer.Serialize(new { error = "limit must be between 1 and 10000." })
            : null;

    // Surface Snowstorm's own message (e.g. ECL syntax errors, term-length limits) so
    // calling agents get something they can act on instead of a masked exception.
    private static string SnowstormErrorJson(System.Net.HttpStatusCode status, string body)
    {
        string? message = null;
        try { message = JsonNode.Parse(body)?["message"]?.ToString(); }
        catch (System.Text.Json.JsonException) { }
        return JsonSerializer.Serialize(new { error = message ?? $"Snowstorm returned HTTP {(int)status}." });
    }


}
