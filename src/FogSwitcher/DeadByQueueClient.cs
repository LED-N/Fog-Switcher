using System.Net.Http.Headers;
using System.Text.Json;

namespace FogSwitcher;

internal enum QueueMode
{
    Live,
    LiveEvent
}

internal static class QueueModeExtensions
{
    public static string ToDisplayName(this QueueMode mode)
    {
        return mode switch
        {
            QueueMode.Live => "Live",
            QueueMode.LiveEvent => "Live Event",
            _ => "Live"
        };
    }
}

internal sealed record QueueRoleTimes(int? KillerSeconds, int? SurvivorSeconds);

internal sealed class DeadByQueueSnapshot
{
    public DateTimeOffset? LastUpdated { get; set; }
    public Dictionary<string, bool> ActiveRegions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<QueueMode, Dictionary<string, QueueRoleTimes>> QueueTimes { get; } = new();
    public bool IsOnline { get; set; }
    public string EventSummary { get; set; } = "No active event detected";
}

internal sealed class DeadByQueueClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public DeadByQueueClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://api2.deadbyqueue.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FogSwitcher", "0.1.0"));
        }
    }

    public async Task<DeadByQueueSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new DeadByQueueSnapshot();

        await PopulateQueuesAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await PopulateRegionsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await PopulateMiscAsync(snapshot, cancellationToken).ConfigureAwait(false);

        return snapshot;
    }

    private async Task PopulateQueuesAsync(DeadByQueueSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var document = await GetJsonDocumentAsync("queues", cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("lastupdated2", out var lastUpdatedElement))
        {
            if (lastUpdatedElement.ValueKind == JsonValueKind.Number && lastUpdatedElement.TryGetInt64(out var unixTime))
            {
                snapshot.LastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime();
            }
            else if (lastUpdatedElement.ValueKind == JsonValueKind.String &&
                     long.TryParse(lastUpdatedElement.GetString(), out unixTime))
            {
                snapshot.LastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime();
            }
        }

        if (!root.TryGetProperty("queues", out var queuesElement) || queuesElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var modeProperty in queuesElement.EnumerateObject())
        {
            if (!TryParseQueueMode(modeProperty.Name, out var queueMode))
            {
                continue;
            }

            var modeDictionary = new Dictionary<string, QueueRoleTimes>(StringComparer.OrdinalIgnoreCase);
            foreach (var regionProperty in modeProperty.Value.EnumerateObject())
            {
                var killerSeconds = TryReadTime(regionProperty.Value, "killer");
                var survivorSeconds = TryReadTime(regionProperty.Value, "survivor");
                modeDictionary[regionProperty.Name] = new QueueRoleTimes(killerSeconds, survivorSeconds);
            }

            snapshot.QueueTimes[queueMode] = modeDictionary;
        }
    }

    private async Task PopulateRegionsAsync(DeadByQueueSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var document = await GetJsonDocumentAsync("regions", cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (!root.TryGetProperty("regions", out var regionsElement) || regionsElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var regionProperty in regionsElement.EnumerateObject())
        {
            if (regionProperty.Value.ValueKind == JsonValueKind.True || regionProperty.Value.ValueKind == JsonValueKind.False)
            {
                snapshot.ActiveRegions[regionProperty.Name] = regionProperty.Value.GetBoolean();
            }
        }
    }

    private async Task PopulateMiscAsync(DeadByQueueSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var document = await GetJsonDocumentAsync("misc", cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        snapshot.IsOnline = root.TryGetProperty("online", out var onlineElement) &&
                            onlineElement.ValueKind == JsonValueKind.True;

        var activeEvents = new List<string>();
        if (root.TryGetProperty("currentEvents", out var eventsElement) && eventsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var eventElement in eventsElement.EnumerateArray())
            {
                if (eventElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        activeEvents.Add(name.Trim());
                    }
                }
            }
        }

        snapshot.EventSummary = activeEvents.Count > 0
            ? string.Join(" | ", activeEvents)
            : "No active event detected";
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseQueueMode(string rawMode, out QueueMode queueMode)
    {
        switch (rawMode)
        {
            case "live":
                queueMode = QueueMode.Live;
                return true;
            case "live-event":
                queueMode = QueueMode.LiveEvent;
                return true;
            default:
                queueMode = QueueMode.Live;
                return false;
        }
    }

    private static int? TryReadTime(JsonElement regionElement, string roleName)
    {
        if (!regionElement.TryGetProperty(roleName, out var roleElement))
        {
            return null;
        }

        if (!roleElement.TryGetProperty("time", out var timeElement))
        {
            return null;
        }

        return timeElement.ValueKind switch
        {
            JsonValueKind.Number when timeElement.TryGetInt32(out var numberValue) => numberValue,
            JsonValueKind.String when int.TryParse(timeElement.GetString(), out var stringValue) => stringValue,
            _ => null
        };
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
