using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConsultologistSnomedMcp;

public class SnowstormFunctions
{
    private const string SnowstormBase = "https://snowstorm.snomed.example.org/MAIN";

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
        [McpToolTrigger("search_concepts", "Search SNOMED CT concepts by term.")] ToolInvocationContext context,
        [McpToolProperty("term",  "string", true,  Description = "Clinical term to search for.")]           string term,
        [McpToolProperty("limit", "number", false, Description = "Maximum results to return (default 10).")] int? limit)
    {
        _logger.LogInformation("search_concepts: term={Term} limit={Limit}", term, limit);
        var url = $"{SnowstormBase}/concepts?term={Uri.EscapeDataString(term ?? "")}&active=true&limit={limit ?? 10}";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_ancestors ─────────────────────────────────────────────────────────

    [Function(nameof(GetAncestors))]
    public async Task<string> GetAncestors(
        [McpToolTrigger("get_ancestors", "Get all ancestors of a SNOMED concept via IS-A hierarchy.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID.")] string conceptId)
    {
        _logger.LogInformation("get_ancestors: {Id}", conceptId);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($">> {conceptId}")}&active=true&limit=50";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_children ──────────────────────────────────────────────────────────

    [Function(nameof(GetChildren))]
    public async Task<string> GetChildren(
        [McpToolTrigger("get_children", "Get direct children of a SNOMED concept.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID.")] string conceptId)
    {
        _logger.LogInformation("get_children: {Id}", conceptId);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString($"<! {conceptId}")}&active=true&limit=50";
        return await FetchConcepts(new Uri(url));
    }

    // ── validate_concept ──────────────────────────────────────────────────────

    [Function(nameof(ValidateConcept))]
    public async Task<string> ValidateConcept(
        [McpToolTrigger("validate_concept", "Check if a SNOMED concept ID is valid and active.")] ToolInvocationContext context,
        [McpToolProperty("concept_id", "string", true, Description = "SNOMED CT concept ID to validate.")] string conceptId)
    {
        _logger.LogInformation("validate_concept: {Id}", conceptId);
        var response = await _http.GetAsync($"{SnowstormBase}/concepts/{conceptId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return """{"valid":false}""";

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
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
        [McpToolTrigger("ecl_query", "Run an arbitrary ECL query against SNOMED CT.")] ToolInvocationContext context,
        [McpToolProperty("ecl",   "string", true,  Description = "SNOMED CT Expression Constraint Language query.")] string ecl,
        [McpToolProperty("limit", "number", false, Description = "Maximum results to return (default 20).")]          int? limit)
    {
        _logger.LogInformation("ecl_query: {Ecl}", ecl);
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString(ecl ?? "")}&active=true&limit={limit ?? 20}";
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

    // ── get_by_semantic_tag ───────────────────────────────────────────────────

    [Function(nameof(GetBySemanticTag))]
    public async Task<string> GetBySemanticTag(
        [McpToolTrigger("get_by_semantic_tag", "Get SNOMED CT concepts by semantic tag (e.g. disorder, finding, procedure).")] ToolInvocationContext context,
        [McpToolProperty("semantic_tag", "string", true,  Description = "Semantic tag to filter by, e.g. disorder, finding, procedure, body structure, substance, organism.")] string semanticTag,
        [McpToolProperty("limit",        "number", false, Description = "Maximum results to return (default 50).")] int? limit)
    {
        _logger.LogInformation("get_by_semantic_tag: tag={Tag} limit={Limit}", semanticTag, limit);

        if (!SemanticTagRoots.TryGetValue(semanticTag?.Trim() ?? "", out var rootId))
            return JsonSerializer.Serialize(new { error = $"Unknown semantic tag '{semanticTag}'. Known tags: {string.Join(", ", SemanticTagRoots.Keys)}" });

        var ecl = $"<< {rootId}";
        var url = $"{SnowstormBase}/concepts?ecl={Uri.EscapeDataString(ecl)}&active=true&limit={limit ?? 50}";
        return await FetchConcepts(new Uri(url));
    }

    // ── get_terminology_info ──────────────────────────────────────────────────

    [Function(nameof(GetTerminologyInfo))]
    public async Task<string> GetTerminologyInfo(
        [McpToolTrigger("get_terminology_info", "Get summary statistics for the loaded SNOMED CT edition: concept counts, semantic tag distribution, and edition metadata.")] ToolInvocationContext context)
    {
        _logger.LogInformation("get_terminology_info");

        // 1. Fetch edition metadata from /codesystems
        var codesysJson = await _http.GetStringAsync($"{SnowstormBase}/codesystems");
        var codesys     = JsonNode.Parse(codesysJson);
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
        var data = JsonNode.Parse(json);
        return data?["total"]?.GetValue<long>() ?? 0;
    }

    private async Task<string> FetchConcepts(Uri uri)
    {
        var json = await _http.GetStringAsync(uri);
        var data = JsonNode.Parse(json);
        var items = data?["items"]?.AsArray()
            .Select(c => new { id = c?["conceptId"]?.ToString(), fsn = c?["fsn"]?["term"]?.ToString() })
            .ToArray();
        return JsonSerializer.Serialize(items);
    }


}
