using System.Net;
using System.Net.NetworkInformation;

namespace FogSwitcher;

internal sealed class LatencyProbeService
{
    public async Task<Dictionary<string, long?>> MeasureAsync(IEnumerable<DbdRegion> regions, CancellationToken cancellationToken = default)
    {
        var tasks = regions.Select(region => MeasureRegionAsync(region, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results.ToDictionary(item => item.Code, item => item.LatencyMs, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<(string Code, long? LatencyMs)> MeasureRegionAsync(DbdRegion region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var address in await ResolveIPv4CandidatesAsync(region, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, 1200).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    return (region.Code, reply.RoundtripTime);
                }
            }
            catch
            {
                // Keep trying the next address candidate.
            }
        }

        return (region.Code, null);
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveIPv4CandidatesAsync(DbdRegion region, CancellationToken cancellationToken)
    {
        var addresses = new List<IPAddress>();

        foreach (var host in new[] { region.ServiceHost, region.PingHost })
        {
            try
            {
                var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                foreach (var address in resolved.Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    if (!addresses.Contains(address))
                    {
                        addresses.Add(address);
                    }
                }
            }
            catch
            {
                // Try the next hostname.
            }
        }

        return addresses;
    }
}
