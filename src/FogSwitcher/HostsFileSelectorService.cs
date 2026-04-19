using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace FogSwitcher;

internal sealed class HostsFileSelectorService
{
    private const string SectionMarker = "# --+ Fog Switcher +--";

    public string HostsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers\\etc\\hosts");

    public bool IsRunningAsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public HashSet<string> ReadAllowedRegionCodes()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var managedBlock = ReadManagedBlock();
        if (string.IsNullOrWhiteSpace(managedBlock))
        {
            return allowed;
        }

        using var reader = new StringReader(managedBlock);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("# region ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4)
            {
                continue;
            }

            var regionCode = tokens[2].Trim();
            var state = tokens[3].Trim();
            if (string.Equals(state, "allow", StringComparison.OrdinalIgnoreCase))
            {
                allowed.Add(regionCode);
            }
        }

        return allowed;
    }

    public void ApplySelection(IEnumerable<string> allowedRegionCodes)
    {
        var allowedSet = new HashSet<string>(allowedRegionCodes, StringComparer.OrdinalIgnoreCase);
        if (allowedSet.Count == 0)
        {
            throw new InvalidOperationException("Select at least one region before applying the hosts lock.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Edited by Fog Switcher (Dead by Daylight server selector)");
        builder.AppendLine("# Checked regions stay open. Unchecked regions are mapped to 0.0.0.0.");
        builder.AppendLine($"# Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        foreach (var region in DbdRegionCatalog.VisibleRegions)
        {
            WriteRegionSection(builder, region, allowedSet.Contains(region.Code));
        }

        builder.AppendLine("# Hidden AWS regions that DbD does not normally use are blocked for stability.");
        foreach (var region in DbdRegionCatalog.AlwaysBlockedRegions)
        {
            WriteRegionSection(builder, region, allow: false);
        }

        WriteWrappedHostsSection(builder.ToString());
        FlushDns();
    }

    public void ClearSelection()
    {
        var original = SafeReadHosts();
        var normalized = NormalizeLineEndings(original);
        var first = normalized.IndexOf(SectionMarker, StringComparison.Ordinal);
        if (first < 0)
        {
            return;
        }

        var second = normalized.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal);
        var updated = second >= 0
            ? normalized.Remove(first, (second + SectionMarker.Length) - first)
            : normalized[..first];

        SafeBackupHosts();
        File.WriteAllText(HostsPath, updated.TrimEnd() + Environment.NewLine);
        FlushDns();
    }

    private static void WriteRegionSection(StringBuilder builder, DbdRegion region, bool allow)
    {
        builder.AppendLine($"# region {region.Code} {(allow ? "allow" : "block")} {region.DisplayName}");
        foreach (var host in region.Hosts)
        {
            builder.AppendLine(allow ? $"# keep {host}" : $"0.0.0.0 {host}");
        }

        builder.AppendLine();
    }

    private string? ReadManagedBlock()
    {
        var original = SafeReadHosts();
        if (string.IsNullOrWhiteSpace(original))
        {
            return null;
        }

        var normalized = NormalizeLineEndings(original);
        var first = normalized.IndexOf(SectionMarker, StringComparison.Ordinal);
        if (first < 0)
        {
            return null;
        }

        var second = normalized.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal);
        if (second < 0)
        {
            return normalized[(first + SectionMarker.Length)..];
        }

        var start = first + SectionMarker.Length;
        return normalized[start..second];
    }

    private void WriteWrappedHostsSection(string innerContent)
    {
        var original = SafeReadHosts();
        var normalized = NormalizeLineEndings(original);

        var first = normalized.IndexOf(SectionMarker, StringComparison.Ordinal);
        var second = first >= 0
            ? normalized.IndexOf(SectionMarker, first + SectionMarker.Length, StringComparison.Ordinal)
            : -1;

        var wrappedBlock = SectionMarker + "\n" + NormalizeLineEndings(innerContent).Trim() + "\n" + SectionMarker + "\n";
        string updated;

        if (first >= 0 && second >= 0)
        {
            updated = normalized[..first] + wrappedBlock + normalized[(second + SectionMarker.Length)..];
        }
        else if (first >= 0)
        {
            updated = normalized[..first] + wrappedBlock;
        }
        else
        {
            var prefix = string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.TrimEnd() + "\n\n";
            updated = prefix + wrappedBlock;
        }

        SafeBackupHosts();
        File.WriteAllText(HostsPath, updated.Replace("\n", Environment.NewLine));
    }

    private void FlushDns()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(3000);
        }
        catch
        {
            // DNS flush is a convenience step only.
        }
    }

    private void SafeBackupHosts()
    {
        try
        {
            File.Copy(HostsPath, HostsPath + ".bak", overwrite: true);
        }
        catch
        {
            // Backup creation is best-effort only.
        }
    }

    private string SafeReadHosts()
    {
        try
        {
            return File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeLineEndings(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
