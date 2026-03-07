namespace SwebKit.Core.Domain;

public class FilterState
{
    public string TimeRange { get; set; } = "1h";
    public List<string> Levels { get; set; } = [];
    public string? TextSearch { get; set; }
    public string? CorrelationId { get; set; }
    public string? OperationName { get; set; }
    public Dictionary<string, string> PropertyFilters { get; set; } = [];
    public string? RawQuery { get; set; }
    public int MaxRows { get; set; } = 200;
}
