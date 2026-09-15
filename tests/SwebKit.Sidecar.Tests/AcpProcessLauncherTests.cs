using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the pure parts of <see cref="AcpProcessLauncher"/> — argument splitting and
/// executable resolution's failure paths. Actually spawning a process is left to the manual
/// end-to-end pass against a real agent (feature test plan).</summary>
public class AcpProcessLauncherTests
{
    [Theory]
    [InlineData("-y @scope/pkg", new[] { "-y", "@scope/pkg" })]
    [InlineData("--acp", new[] { "--acp" })]
    [InlineData("  spaced   out  ", new[] { "spaced", "out" })]
    [InlineData("", new string[0])]
    public void SplitArguments_splits_on_whitespace(string args, string[] expected)
    {
        Assert.Equal(expected, AcpProcessLauncher.SplitArguments(args));
    }

    [Fact]
    public void SplitArguments_keeps_quoted_segments_together()
    {
        Assert.Equal(
            new[] { "-c", "C:\\Program Files\\agent\\run.js" },
            AcpProcessLauncher.SplitArguments("-c \"C:\\Program Files\\agent\\run.js\""));
    }

    [Fact]
    public void SplitArguments_treats_single_quotes_as_literal()
    {
        // A config field, not a shell — 'x y' is three chars' worth of literal content, not a quote.
        Assert.Equal(new[] { "'a", "b'" }, AcpProcessLauncher.SplitArguments("'a b'"));
    }

    [Fact]
    public void ResolveExecutable_throws_for_an_empty_command()
    {
        Assert.Throws<FileNotFoundException>(() => AcpProcessLauncher.ResolveExecutable("  "));
    }

    [Fact]
    public void ResolveExecutable_throws_for_a_nonexistent_rooted_path()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "agent.exe");

        var ex = Assert.Throws<FileNotFoundException>(() => AcpProcessLauncher.ResolveExecutable(missing));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void ResolveExecutable_throws_a_user_facing_message_for_a_command_not_on_path()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => AcpProcessLauncher.ResolveExecutable($"swebkit-no-such-agent-{Guid.NewGuid():N}"));
        Assert.Contains("PATH", ex.Message);
    }

    [Fact]
    public void ResolveExecutable_prefers_pathext_candidate_over_extensionless_file()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Node's bin dir ships a POSIX `npx` script next to `npx.cmd`; the extensionless file
        // exists but cannot be launched by CreateProcess, so the .cmd must win.
        var dir = CreateTempDir();
        var bare = Path.Combine(dir, "agent");
        var cmd = Path.Combine(dir, "agent.cmd");
        File.WriteAllText(bare, "#!/bin/sh\necho not-windows\n");
        File.WriteAllText(cmd, "@echo off\r\n");

        // PATHEXT on Windows is typically uppercase (.CMD), so the resolved path keeps that
        // casing even though the file on disk is agent.cmd — compare case-insensitively.
        Assert.Equal(cmd, AcpProcessLauncher.ResolveExecutable(bare), ignoreCase: true);
    }

    [Fact]
    public void ResolveExecutable_falls_back_to_bare_extensionless_file()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = CreateTempDir();
        var bare = Path.Combine(dir, "agent");
        File.WriteAllText(bare, "pretend-pe-image");

        Assert.Equal(bare, AcpProcessLauncher.ResolveExecutable(bare));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"acp-launcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
