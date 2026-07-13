using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Constants;
using SwebKit.Core.Models;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace SwebKit.Kubernetes.AksClient;

public partial class KubernetesAksClient
{
    // ── Wave 3: Helm preview ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a ProcessStartInfo for helm that reads PATH from the registry at call time,
    /// ensuring newly installed binaries are found even if the app process predates the install.
    /// </summary>
    private static ProcessStartInfo HelmStartInfo(string arguments)
    {
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var fullPath = $"{machinePath};{userPath}";

        var psi = new ProcessStartInfo
        {
            FileName = "helm",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["PATH"] = fullPath;
        return psi;
    }

    public async Task<HelmDiffPreview> PreviewHelmUpgradeAsync(
        string ns,
        string releaseName,
        CancellationToken ct = default)
    {
        try
        {
            // Check if helm binary is available
            using var helmCheck = new Process { StartInfo = HelmStartInfo("version --short") };

            bool helmAvailable;
            try
            {
                helmCheck.Start();
                await helmCheck.WaitForExitAsync(ct).ConfigureAwait(false);
                helmAvailable = helmCheck.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                helmAvailable = false;
            }

            if (!helmAvailable)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Unsupported,
                    CapabilityNote = "The 'helm' binary was not found. Install Helm to enable diff preview.",
                    Findings = ["helm binary not found on PATH."]
                };
            }

            // Check if helm-diff plugin is installed
            using var pluginCheck = new Process { StartInfo = HelmStartInfo("plugin list") };

            pluginCheck.Start();
            var pluginOutput = await pluginCheck.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await pluginCheck.WaitForExitAsync(ct).ConfigureAwait(false);
            var hasDiffPlugin = pluginOutput.Contains("diff", StringComparison.OrdinalIgnoreCase);

            if (!hasDiffPlugin)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Degraded,
                    CapabilityNote = "The helm-diff plugin is not installed. Install it with: helm plugin install https://github.com/databus23/helm-diff",
                    Findings = ["helm-diff plugin not found. Run 'helm plugin install https://github.com/databus23/helm-diff' to enable full diff preview."]
                };
            }

            // Run helm diff upgrade
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var diffProcess = new Process
            {
                StartInfo = HelmStartInfo($"diff upgrade --namespace {ns} {releaseName} --reuse-values")
            };

            diffProcess.Start();
            var diffOutput = await diffProcess.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            var diffError = await diffProcess.StandardError.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await diffProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (diffProcess.ExitCode != 0)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Degraded,
                    CapabilityNote = "helm diff returned a non-zero exit code.",
                    Findings = string.IsNullOrWhiteSpace(diffError)
                        ? ["helm diff exited with a non-zero code without additional output."]
                        : [diffError.Trim()]
                };
            }

            return new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Full,
                CapabilityNote = "helm diff upgrade completed successfully.",
                DiffText = string.IsNullOrWhiteSpace(diffOutput) ? "(no changes detected)" : diffOutput,
                Findings = string.IsNullOrWhiteSpace(diffOutput)
                    ? ["No changes detected between the current release and re-applying with the same values."]
                    : ["Diff output generated. Review DiffText for the proposed changes."]
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Degraded,
                CapabilityNote = $"Helm diff preview failed: {ex.Message}",
                Findings = [$"Degraded: {ex.Message}"]
            };
        }
    }

    public async Task<HelmDiffPreview> PreviewHelmRollbackAsync(
        string ns,
        string releaseName,
        int revision,
        CancellationToken ct = default)
    {
        try
        {
            // Check if helm binary is available
            using var helmCheck = new Process { StartInfo = HelmStartInfo("version --short") };

            bool helmAvailable;
            try
            {
                helmCheck.Start();
                await helmCheck.WaitForExitAsync(ct).ConfigureAwait(false);
                helmAvailable = helmCheck.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                helmAvailable = false;
            }

            if (!helmAvailable)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Unsupported,
                    CapabilityNote = "The 'helm' binary was not found. Install Helm to enable rollback diff preview.",
                    Findings = ["helm binary not found on PATH."]
                };
            }

            // Check if helm-diff plugin is installed
            using var pluginCheck = new Process { StartInfo = HelmStartInfo("plugin list") };

            pluginCheck.Start();
            var pluginOutput = await pluginCheck.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await pluginCheck.WaitForExitAsync(ct).ConfigureAwait(false);
            var hasDiffPlugin = pluginOutput.Contains("diff", StringComparison.OrdinalIgnoreCase);

            if (!hasDiffPlugin)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Degraded,
                    CapabilityNote = "The helm-diff plugin is not installed. Install it with: helm plugin install https://github.com/databus23/helm-diff",
                    Findings = ["helm-diff plugin not found. Run 'helm plugin install https://github.com/databus23/helm-diff' to enable rollback diff preview."]
                };
            }

            // Run helm diff rollback
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var diffProcess = new Process
            {
                StartInfo = HelmStartInfo($"diff rollback --namespace {ns} {releaseName} {revision}")
            };

            diffProcess.Start();
            var diffOutput = await diffProcess.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            var diffError = await diffProcess.StandardError.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await diffProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (diffProcess.ExitCode != 0)
            {
                return new HelmDiffPreview
                {
                    Namespace = ns,
                    ReleaseName = releaseName,
                    Capability = HelmPreviewCapability.Degraded,
                    CapabilityNote = "helm diff rollback returned a non-zero exit code.",
                    Findings = string.IsNullOrWhiteSpace(diffError)
                        ? ["helm diff rollback exited with a non-zero code without additional output."]
                        : [diffError.Trim()]
                };
            }

            return new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Full,
                CapabilityNote = $"helm diff rollback to revision {revision} completed successfully.",
                DiffText = string.IsNullOrWhiteSpace(diffOutput) ? "(no changes detected)" : diffOutput,
                Findings = string.IsNullOrWhiteSpace(diffOutput)
                    ? [$"No changes detected between the current release and revision {revision}."]
                    : [$"Diff output generated for rollback to revision {revision}. Review DiffText for the proposed changes."]
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HelmDiffPreview
            {
                Namespace = ns,
                ReleaseName = releaseName,
                Capability = HelmPreviewCapability.Degraded,
                CapabilityNote = $"Helm rollback diff preview failed: {ex.Message}",
                Findings = [$"Degraded: {ex.Message}"]
            };
        }
    }
}
