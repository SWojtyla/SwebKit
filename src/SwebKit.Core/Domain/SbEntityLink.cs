namespace SwebKit.Core.Domain;

/// <summary>Links a project environment to a specific queue, topic, or subscription inside a global namespace.</summary>
public class SbEntityLink
{
    public Guid NamespaceId { get; set; }
    public string EntityPath { get; set; } = string.Empty;
    public string? Alias { get; set; }
}
