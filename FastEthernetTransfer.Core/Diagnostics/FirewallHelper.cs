using System;
using System.Diagnostics;
using System.IO;

namespace FastEthernetTransfer.Core.Diagnostics;

public static class FirewallHelper
{
    private const string RuleName = "Fast Ethernet File Transfer";

    public static bool AddFirewallRule(out string outputMessage)
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                outputMessage = "Executable path could not be resolved.";
                return false;
            }

            // Command to add inbound firewall rule allowing all TCP and UDP traffic for this executable
            string command = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any";

            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = command,
                Verb = "runas", // Requests administrative elevation if required
                UseShellExecute = true,
                CreateNoWindow = true
            };

            var process = Process.Start(startInfo);
            process?.WaitForExit(5000);

            outputMessage = "Firewall rule successfully configured!";
            Logger.Info($"Firewall rule added for {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            outputMessage = $"Unable to add firewall rule automatically: {ex.Message}";
            Logger.Warn($"Firewall configuration skipped or denied: {ex.Message}");
            return false;
        }
    }

    public static string GetManualFirewallCommand()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "FastEthernetTransfer.exe";
        return $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" enable=yes";
    }
}
