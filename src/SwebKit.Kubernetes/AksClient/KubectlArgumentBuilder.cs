using System.Text.RegularExpressions;

namespace SwebKit.Kubernetes.AksClient;

/// <summary>
/// Builds kubectl/helm arguments as a list of strings for use with
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>,
/// eliminating shell injection and quoting risks.
/// </summary>
internal sealed class KubectlArgumentBuilder
{
    private readonly List<string> _args = [];

    /// <summary>
    /// Validates a Kubernetes resource name (DNS-1123 label/subdomain).
    /// Throws <see cref="ArgumentException"/> if invalid.
    /// </summary>
    public static string ValidateKubernetesName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 ||
            !KubernetesNamePattern.IsMatch(value))
            throw new ArgumentException($"Invalid Kubernetes {paramName} name: '{value}'.", paramName);
        return value;
    }

    private static readonly Regex KubernetesNamePattern =
        new("^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>
    /// Adds global kubectl flags (--kubeconfig, --context) before the subcommand.
    /// Call this first, before adding the subcommand.
    /// </summary>
    public KubectlArgumentBuilder WithGlobalFlags(string? kubeconfigPath, string? context)
    {
        if (!string.IsNullOrWhiteSpace(kubeconfigPath))
        {
            _args.Add("--kubeconfig");
            _args.Add(kubeconfigPath);
        }
        if (!string.IsNullOrWhiteSpace(context))
        {
            _args.Add("--context");
            _args.Add(context);
        }
        return this;
    }

    /// <summary>
    /// Adds global helm flags (--kubeconfig, --kube-context) before the subcommand.
    /// </summary>
    public KubectlArgumentBuilder WithHelmGlobalFlags(string? kubeconfigPath, string? context)
    {
        if (!string.IsNullOrWhiteSpace(kubeconfigPath))
        {
            _args.Add("--kubeconfig");
            _args.Add(kubeconfigPath);
        }
        if (!string.IsNullOrWhiteSpace(context))
        {
            _args.Add("--kube-context");
            _args.Add(context);
        }
        return this;
    }

    public KubectlArgumentBuilder Add(string arg)
    {
        _args.Add(arg);
        return this;
    }

    public KubectlArgumentBuilder AddRange(IEnumerable<string> args)
    {
        _args.AddRange(args);
        return this;
    }

    /// <summary>
    /// Adds the port-forward subcommand with validated arguments.
    /// </summary>
    public KubectlArgumentBuilder PortForward(string ns, string resourceName, int localPort, int remotePort)
    {
        ValidateKubernetesName(ns, nameof(ns));
        _args.Add("port-forward");
        _args.Add(resourceName);
        _args.Add($"{localPort}:{remotePort}");
        _args.Add("-n");
        _args.Add(ns);
        return this;
    }

    /// <summary>
    /// Adds the exec -it subcommand with validated arguments.
    /// </summary>
    public KubectlArgumentBuilder ExecInteractive(string ns, string pod, string container)
    {
        ValidateKubernetesName(ns, nameof(ns));
        ValidateKubernetesName(pod, nameof(pod));
        ValidateKubernetesName(container, nameof(container));
        _args.Add("exec");
        _args.Add("-it");
        _args.Add(pod);
        _args.Add("-n");
        _args.Add(ns);
        _args.Add("-c");
        _args.Add(container);
        return this;
    }

    /// <summary>
    /// Adds the apply subcommand.
    /// </summary>
    public KubectlArgumentBuilder Apply(string filePath, string ns)
    {
        _args.Add("apply");
        _args.Add("-f");
        _args.Add(filePath);
        _args.Add("--namespace");
        _args.Add(ns);
        return this;
    }

    /// <summary>
    /// Adds the --dry-run=server flag.
    /// </summary>
    public KubectlArgumentBuilder DryRunServer()
    {
        _args.Add("--dry-run=server");
        return this;
    }

    /// <summary>
    /// Adds a --token flag.
    /// </summary>
    public KubectlArgumentBuilder WithToken(string token)
    {
        _args.Add("--token");
        _args.Add(token);
        return this;
    }

    public IReadOnlyList<string> Build() => _args;
}
