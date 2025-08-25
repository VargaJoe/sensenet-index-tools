namespace WebApp.Models;

public class LastActivityIdComparison
{
    public long? IndexValue { get; set; }
    public long? DatabaseValue { get; set; }
    public int[]? IndexGaps { get; set; }
    public string? IndexPath { get; set; }
    public string? ConnectionString { get; set; }
    public DateTime? IndexRetrievedAt { get; set; }
    public DateTime? DatabaseRetrievedAt { get; set; }
    
    public bool IsMatch => IndexValue.HasValue && DatabaseValue.HasValue && IndexValue == DatabaseValue;
    public string Status => IsMatch ? "Match" : "Mismatch";
    public long? Difference => (IndexValue.HasValue && DatabaseValue.HasValue) ? Math.Abs(IndexValue.Value - DatabaseValue.Value) : null;
    
    public bool HasIndexValue => IndexValue.HasValue;
    public bool HasDatabaseValue => DatabaseValue.HasValue;
    public bool HasBothValues => HasIndexValue && HasDatabaseValue;
}
