using System;

namespace SenseNetIndexTools
{
    public class ContentItem
    {
        public int NodeId { get; set; }
        public int VersionId { get; set; }
        public long TimestampNumeric { get; set; } // SQL Server rowversion/timestamp value
        public long VersionTimestampNumeric { get; set; } // Numeric representation of the version timestamp
        public string Path { get; set; } = string.Empty;
        public string? NodeType { get; set; }
        public bool InDatabase { get; set; }
        public bool InIndex { get; set; }
        public string? IndexNodeId { get; set; }
        public string? IndexVersionId { get; set; }
        public string? IndexTimestamp { get; set; }
        public string? IndexVersionTimestamp { get; set; } 
        
        public string Status
        {
            get
            {
                if (!InDatabase) return "Index only";
                if (!InIndex) return "DB only";

                bool idsMatch = string.Equals(NodeId.ToString(), IndexNodeId) &&
                               string.Equals(VersionId.ToString(), IndexVersionId);

                // For timestamp comparison
                bool timestampMatch = false;
                if (!string.IsNullOrEmpty(IndexTimestamp) && TimestampNumeric > 0)
                {
                    // Debug timestamp comparison
                    if (ContentComparer.VerboseLogging)
                    {
                        Console.WriteLine($"TIMESTAMP COMPARISON:");
                        Console.WriteLine($"  DB Timestamp (raw numeric): {TimestampNumeric}");
                        Console.WriteLine($"  Index Timestamp (raw string): {IndexTimestamp}");
                    }
                    
                    // Directly compare the numeric values - both are bigint values
                    if (long.TryParse(IndexTimestamp, out long indexTimestampNumeric))
                    {
                        timestampMatch = (indexTimestampNumeric == TimestampNumeric);
                        if (ContentComparer.VerboseLogging)
                        {
                            Console.WriteLine($"  Index Timestamp (parsed numeric): {indexTimestampNumeric}");
                            Console.WriteLine($"  Comparison result: {(timestampMatch ? "MATCH" : "MISMATCH")}");
                        }
                    }
                    else if (ContentComparer.VerboseLogging)
                    {
                        Console.WriteLine($"  Failed to parse index timestamp as numeric value");
                    }
                }
                else if (idsMatch)
                {
                    // If IDs match but we can't compare timestamps (missing or invalid), assume it's a match
                    timestampMatch = true;
                    if (ContentComparer.VerboseLogging)
                    {
                        Console.WriteLine($"TIMESTAMP COMPARISON: Assuming match due to matching IDs and missing/invalid timestamp data");
                        Console.WriteLine($"  DB Timestamp: {TimestampNumeric}");
                        Console.WriteLine($"  Index Timestamp: {IndexTimestamp}");
                    }
                }

                if (idsMatch && timestampMatch) return "Match";
                if (idsMatch && !timestampMatch) return "Timestamp mismatch";
                return "ID mismatch";
            }
        }
        
        public override string ToString()
        {
            return $"{(InDatabase ? NodeId.ToString() : "-")}\t{(InDatabase ? VersionId.ToString() : "-")}\t" +
                   $"{(InDatabase ? TimestampNumeric.ToString() : "-")}\t" +
                   $"{(InIndex ? IndexNodeId : "-")}\t{(InIndex ? IndexVersionId : "-")}\t" +
                   $"{(InIndex ? IndexTimestamp : "-")}\t" +
                   $"{Path}\t{NodeType}\t{Status}";
        }
    }
}
