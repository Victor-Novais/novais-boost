using System.Text.Json;

namespace NovaisBoost.Core;

/// <summary>A cloud region reached by a TCP connection (cloud endpoints often drop ICMP ping).</summary>
public sealed record ServerRegion(string Id, string Name, string Host, int Port);

/// <param name="ValveSdrAppId">For Valve games: the relays come from the public SDR configuration of this app id.</param>
public sealed record GameServers(string Game, string[] Regions, string Note, int? ValveSdrAppId = null);

public sealed record ServerCatalog(ServerRegion[] Regions, GameServers[] Games);

/// <summary>One Valve relay point of presence (e.g. "gru" São Paulo) and one of its relay addresses.</summary>
public sealed record SdrPop(string Code, string Description, string Address);

public static class GameServerCatalog
{
    public static string SdrUrl(int appId) => $"https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid={appId}";

    /// <summary>Parses GetSDRConfig: { "pops": { "gru": { "desc": "Sao Paulo", "relays": [ { "ipv4": "..." } ] } } }. First relay per POP.</summary>
    public static IReadOnlyList<SdrPop> ParseSdr(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("pops", out var pops) || pops.ValueKind != JsonValueKind.Object)
            return [];
        var result = new List<SdrPop>();
        foreach (var pop in pops.EnumerateObject())
        {
            if (!pop.Value.TryGetProperty("relays", out var relays) || relays.ValueKind != JsonValueKind.Array)
                continue;
            var address = relays.EnumerateArray().Select(relay => relay.TryGetProperty("ipv4", out var ip) ? ip.GetString() : null)
                .FirstOrDefault(ip => System.Net.IPAddress.TryParse(ip, out _));
            if (address is null)
                continue;
            var description = pop.Value.TryGetProperty("desc", out var desc) ? desc.GetString() ?? pop.Name : pop.Name;
            result.Add(new SdrPop(pop.Name, description, address));
        }
        return result;
    }

    /// <summary>The closest servers first; unreachable ones (no reply) at the end.</summary>
    public static IReadOnlyList<(string Name, PingStats Stats)> Rank(IEnumerable<(string Name, PingStats Stats)> results) =>
        results.OrderBy(result => result.Stats.Received == 0).ThenBy(result => result.Stats.AverageMs).ToArray();
}
