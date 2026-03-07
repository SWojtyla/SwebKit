namespace SwebKit.Core.Domain;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string IconColor { get; set; } = "#0078D4";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectEnvironment> Environments { get; set; } = [];
}
