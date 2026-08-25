using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ConsultologistSnomedMcp;

/// <summary>
/// What this server serves, stated by the server: the SNOMED CT edition
/// Snowstorm has loaded (edition, version, import date — from /codesystems,
/// no count queries) and this build's own version and commit. Anonymous and
/// open-CORS: deployment facts only, no clinical data. Consumers: the
/// Consultologist engine, which stamps the edition and the server build on
/// every job record (Consultologist-Blazor#403), and anyone checking a record.
/// </summary>
public sealed record TerminologyInfoResponse(
    string? Edition,
    string? Version,
    string? ImportDate,
    string? ServerVersion,
    string? Commit,
    DateTimeOffset GeneratedAtUtc);

public sealed class TerminologyInfo
{
    private static readonly string SnowstormRoot =
        (Environment.GetEnvironmentVariable("SNOWSTORM_URL") ?? "https://snowstorm.snomed.example.org").TrimEnd('/');
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private const int FullCommitLength = 40;

    private readonly HttpClient _http;
    private readonly ILogger<TerminologyInfo> _logger;
    private (TerminologyInfoResponse Response, DateTimeOffset FetchedAt)? _cache;

    public TerminologyInfo(IHttpClientFactory httpClientFactory, ILogger<TerminologyInfo> logger)
    {
        _http = httpClientFactory.CreateClient();
        _logger = logger;
    }

    [Function("PublicTerminology")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "Public/Terminology")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var options = req.CreateResponse(HttpStatusCode.NoContent);
            ApplyCors(options);
            return options;
        }

        TerminologyInfoResponse info;
        try
        {
            if (_cache is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < CacheDuration)
            {
                info = cached.Response;
            }
            else
            {
                var codesystems = JsonNode.Parse(await _http.GetStringAsync($"{SnowstormRoot}/codesystems", cancellationToken));
                info = Describe(codesystems, InformationalVersion(), DateTimeOffset.UtcNow);
                _cache = (info, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Snowstorm /codesystems unavailable for Public/Terminology.");
            var unavailable = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            ApplyCors(unavailable);
            await unavailable.WriteAsJsonAsync(new { error = "The terminology server could not read its own edition." }, cancellationToken);
            return unavailable;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        ApplyCors(response);
        response.Headers.Add("Cache-Control", "public, max-age=60");
        await response.WriteAsJsonAsync(info, cancellationToken);
        return response;
    }

    /// <summary>The rule, separated from HTTP so it can be tested on JSON and version strings.</summary>
    public static TerminologyInfoResponse Describe(JsonNode? codesystems, string? informationalVersion, DateTimeOffset now)
    {
        var latest = codesystems?["items"]?[0]?["latestVersion"];
        var raw = string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion;
        var separator = raw.IndexOf('+');

        return new TerminologyInfoResponse(
            latest?["description"]?.ToString(),
            latest?["version"]?.ToString(),
            latest?["importDate"]?.ToString(),
            separator < 0 ? raw : raw[..separator],
            CommitOf(informationalVersion),
            now);
    }

    /// <summary>The full commit the build stamped as +metadata (-p:SourceRevisionId in deploy.yml), or null.</summary>
    public static string? CommitOf(string? informationalVersion)
    {
        if (informationalVersion == null)
        {
            return null;
        }

        var separator = informationalVersion.IndexOf('+');
        if (separator < 0)
        {
            return null;
        }

        foreach (var token in informationalVersion[(separator + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == FullCommitLength && token.All(Uri.IsHexDigit))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string? InformationalVersion() =>
        typeof(TerminologyInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static void ApplyCors(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
    }
}
