using System.Diagnostics;
using System.Text;
using SwebKit.Core.Domain;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>
/// Spawns ACP agent processes from an <see cref="AgentProfile"/>. Owns the two Windows-specific
/// quirks that make naive <c>ProcessStartInfo</c> fail here: <c>npx</c>/<c>gemini</c> are
/// <c>.cmd</c> shims (CreateProcess only resolves <c>.exe</c> implicitly), and a console child of
/// a windowed app pops an empty console window without <see cref="ProcessStartInfo.CreateNoWindow"/>.
/// </summary>
public static class AcpProcessLauncher
{
    /// <summary>Resolves <see cref="AgentProfile.Command"/>, spawns the process with redirected
    /// stdio, and starts a background pump forwarding stderr lines to <paramref name="log"/>.
    /// <paramref name="credentialSecret"/> (already resolved from the credential store by the
    /// caller — never logged) is injected under <see cref="AgentProfile.CredentialEnvVar"/>.</summary>
    /// <exception cref="FileNotFoundException">The command could not be resolved on PATH/PATHEXT —
    /// the message is written to be user-facing ("install Node.js"-style guidance).</exception>
    public static Process Start(AgentProfile profile, string? credentialSecret, ILogger log)
    {
        var executable = ResolveExecutable(profile.Command);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in SplitArguments(profile.Arguments))
            psi.ArgumentList.Add(arg);

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
            psi.WorkingDirectory = profile.WorkingDirectory;

        foreach (var (key, value) in profile.EnvironmentVariables)
            psi.Environment[key] = value;

        if (!string.IsNullOrEmpty(credentialSecret) && !string.IsNullOrEmpty(profile.CredentialEnvVar))
            psi.Environment[profile.CredentialEnvVar] = credentialSecret;

        // Note on the command-injection class of concern here (SAST flags Process.Start): the
        // command/args are the user's own local profile configuration — spawning a configured
        // executable is the feature itself, identical to Zed/VS Code launching a configured agent.
        // UseShellExecute=false + ArgumentList (never a shell string) means nothing here is
        // shell-interpreted, so metacharacters in Arguments cannot escape into a second command.
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start ACP agent '{profile.Command}'.");

        // ACP lets agents write human logs to stderr — forward them into the sidecar log instead
        // of discarding, which is the only diagnostics available when an agent misbehaves.
        _ = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                log.LogDebug("[acp-agent:{Command}] {Line}", profile.Command, line);
        });

        return process;
    }

    /// <summary>Resolves a command to an executable path. Rooted or directory-qualified commands
    /// are used as-is (PATHEXT applied if extensionless); bare names are searched across PATH ×
    /// PATHEXT so <c>npx</c> finds <c>npx.cmd</c> on Windows. Non-Windows simply returns the name —
    /// the OS's own PATH lookup handles it.</summary>
    internal static string ResolveExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new FileNotFoundException("ACP profile has no command configured.");

        if (!OperatingSystem.IsWindows())
            return command;

        var hasDirectory = command.Contains(Path.DirectorySeparatorChar) ||
                           command.Contains(Path.AltDirectorySeparatorChar) ||
                           Path.IsPathRooted(command);

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<string> Candidates()
        {
            if (Path.HasExtension(command))
            {
                yield return command;
            }
            else
            {
                yield return command;
                foreach (var ext in extensions)
                    yield return command + ext;
            }
        }

        if (hasDirectory)
        {
            var match = Candidates().FirstOrDefault(File.Exists);
            if (match is not null)
                return Path.GetFullPath(match);
            throw new FileNotFoundException($"ACP agent command not found: '{command}'.");
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in pathDirs)
        {
            foreach (var candidate in Candidates())
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full))
                    return full;
            }
        }

        throw new FileNotFoundException(
            $"ACP agent command '{command}' was not found on PATH. If this profile uses npx, install " +
            "Node.js first; if it names a CLI (e.g. gemini), install that CLI and restart SwebKit.");
    }

    /// <summary>Splits a single argument string on whitespace, honoring double-quoted segments
    /// (single quotes are literal — this is a config field, not a shell). Backslashes are not
    /// treated as escapes: on Windows, <c>"C:\Program Files\x"</c> inside quotes is the common
    /// case and naive backslash-escaping would corrupt it.</summary>
    internal static List<string> SplitArguments(string? arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
            return result;

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }
}
