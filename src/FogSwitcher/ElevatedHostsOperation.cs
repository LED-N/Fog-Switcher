using System.ComponentModel;
using System.Diagnostics;

namespace FogSwitcher;

internal static class ElevatedHostsOperation
{
    private const string ApplySelectionArgument = "--apply-selection";
    private const string ClearSelectionArgument = "--clear-selection";

    public static bool TryHandleCommand(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
        {
            return false;
        }

        try
        {
            var hostsService = new HostsFileSelectorService();

            switch (args[0])
            {
                case ApplySelectionArgument:
                    hostsService.ApplySelection(ParseSelectionPayload(args));
                    return true;
                case ClearSelectionArgument:
                    hostsService.ClearSelection();
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            exitCode = 1;
            return true;
        }
    }

    public static bool TryApplySelection(IReadOnlyCollection<string> selectedRegionCodes, out string? errorMessage)
    {
        if (selectedRegionCodes.Count == 0)
        {
            errorMessage = "Check at least one region before applying.";
            return false;
        }

        return TryRunElevatedProcess(
            ApplySelectionArgument,
            string.Join(",", selectedRegionCodes),
            out errorMessage);
    }

    public static bool TryClearSelection(out string? errorMessage)
    {
        return TryRunElevatedProcess(ClearSelectionArgument, null, out errorMessage);
    }

    private static bool TryRunElevatedProcess(string commandArgument, string? payload, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = payload is null ? commandArgument : $"{commandArgument} \"{payload}\"",
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process is null)
            {
                errorMessage = "Windows could not start the elevated helper.";
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return true;
            }

            errorMessage = "The elevated helper could not update the hosts file.";
            return false;
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            errorMessage = "The administrator prompt was cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static IReadOnlyList<string> ParseSelectionPayload(string[] args)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            throw new ArgumentException("Missing region list for apply selection.");
        }

        return args[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
