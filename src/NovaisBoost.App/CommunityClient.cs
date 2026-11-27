using System.Net.Http;
using System.Text;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>F21 client: POST /v1/results after an A/B test, GET /v1/evidence?cpu=&amp;gpu= for the review. Opt-in only.</summary>
internal static class CommunityClient
{
    /// <summary>
    /// Address of the deployed backend (backend/README.md). Null until the service exists: then nothing is ever sent and the
    /// consent screen is not shown. community.endpoint in settings.json overrides it.
    /// </summary>
    public const string? DefaultEndpoint = null;

    public static string? Configured(AppSettings settings) => (settings.CommunityOrDefault.Endpoint ?? DefaultEndpoint)?.TrimEnd('/');

    private static string? Endpoint(AppServices app) =>
        app.Settings is var settings && settings.CommunityOrDefault.Enabled ? Configured(settings) : null;

    public static void Send(AppServices app, IReadOnlyList<LocalEvidenceEntry> entries)
    {
        if (Endpoint(app) is not { } endpoint || entries.Count == 0)
            return;
        try
        {
            var json = Community.ToJson(Community.Build(app.Pc, entries, Program.Version));
            using var http = Client();
            using var response = http.PostAsync($"{endpoint}/v1/results", new StringContent(json, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            app.Log.Info($"Base anônima: resultado enviado ({(int)response.StatusCode}).");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            app.Log.Info($"Base anônima: envio não concluído ({error.Message}).");
        }
    }

    public static IReadOnlyDictionary<string, CommunityEvidence> Query(AppServices app)
    {
        if (Endpoint(app) is not { } endpoint)
            return new Dictionary<string, CommunityEvidence>();
        try
        {
            var gpu = app.Pc.Gpus.FirstOrDefault(item => !item.IsIntegrated) ?? app.Pc.Gpus.FirstOrDefault();
            var url = $"{endpoint}/v1/evidence?cpu={Uri.EscapeDataString(Community.Model(app.Pc.Cpu?.Name))}&gpu={Uri.EscapeDataString(Community.Model(gpu?.Name))}";
            using var http = Client();
            return Community.ParseEvidence(http.GetStringAsync(url).GetAwaiter().GetResult())
                .GroupBy(item => item.Tweak, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            app.Log.Info($"Base anônima: consulta não concluída ({error.Message}).");
            return new Dictionary<string, CommunityEvidence>();
        }
    }

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"NOVAIS-boost/{Program.Version}");
        return http;
    }
}
