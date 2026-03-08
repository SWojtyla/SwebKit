namespace SwebKit.Core.Domain;

public class SbMessageTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? Subject { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
