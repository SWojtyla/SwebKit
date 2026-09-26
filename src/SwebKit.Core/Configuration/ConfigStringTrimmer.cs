using System.Collections;
using System.Reflection;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Trims edge whitespace off every writable string in the profile graph.
///
/// Settings values are identifiers — URLs, hosts, connection strings, credential keys,
/// resource ids — where a stray leading/trailing space is never meaningful but is
/// invisible in the UI and breaks connection tests and client construction when it
/// arrives via copy/paste or a hand-edited <c>profiles.json</c>. Normalizing at the
/// persistence boundary makes "stored config never carries edge whitespace" an
/// invariant for every writer: the React settings forms, config import, direct JSON
/// edits, and any future input.
/// </summary>
internal static class ConfigStringTrimmer
{
    public static void Trim(object? root)
    {
        if (root is null or string)
        {
            return;
        }

        Visit(root, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static void Visit(object? node, HashSet<object> visited)
    {
        if (node is null or string || !visited.Add(node))
        {
            return;
        }

        switch (node)
        {
            case IList<string> strings:
                for (var i = 0; i < strings.Count; i++)
                {
                    strings[i] = strings[i]?.Trim() ?? string.Empty;
                }
                return;
            case IDictionary dictionary:
                // Values only — rewriting keys could collide two entries into one. Writing
                // back by key is safe: the key set never changes during the pass.
                foreach (var key in dictionary.Keys)
                {
                    var value = dictionary[key];
                    if (value is string text && text != text.Trim())
                    {
                        dictionary[key] = text.Trim();
                    }
                    else
                    {
                        Visit(value, visited);
                    }
                }
                return;
            case IEnumerable sequence:
                foreach (var item in sequence)
                {
                    Visit(item, visited);
                }
                return;
        }

        // Only walk our own domain objects — never reflect into framework types.
        var type = node.GetType();
        if (type.FullName?.StartsWith("SwebKit.", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (prop.PropertyType == typeof(string))
            {
                if (prop.CanWrite && prop.GetValue(node) is string value && value != value.Trim())
                {
                    prop.SetValue(node, value.Trim());
                }
                continue;
            }

            Visit(prop.GetValue(node), visited);
        }
    }
}
