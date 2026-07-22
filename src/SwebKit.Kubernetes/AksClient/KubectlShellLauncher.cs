using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SwebKit.Kubernetes.AksClient;

internal static class KubectlShellLauncher
{
    public static void Launch(IReadOnlyList<string> kubectlArguments)
    {
        try
        {
            Start(CreateWindowsTerminalStartInfo(kubectlArguments), "Windows Terminal");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            Start(CreatePowerShellStartInfo(kubectlArguments), "Windows PowerShell");
        }
    }

    internal static ProcessStartInfo CreateWindowsTerminalStartInfo(IReadOnlyList<string> kubectlArguments)
    {
        var startInfo = new ProcessStartInfo("wt.exe")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("kubectl.exe");
        foreach (var argument in kubectlArguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    internal static ProcessStartInfo CreatePowerShellStartInfo(IReadOnlyList<string> kubectlArguments)
    {
        var argumentsJson = JsonSerializer.Serialize(kubectlArguments);
        var argumentsPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(argumentsJson));
        var script = $"$json=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{argumentsPayload}'));$kubectlArgs=ConvertFrom-Json -InputObject $json;& kubectl.exe @kubectlArgs";
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        return new ProcessStartInfo("powershell.exe", $"-NoExit -EncodedCommand {encodedCommand}")
        {
            UseShellExecute = true
        };
    }

    private static void Start(ProcessStartInfo startInfo, string hostName)
    {
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException($"Failed to start {hostName} for the kubectl shell.");
    }
}
