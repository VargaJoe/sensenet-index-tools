using System;

namespace SenseNetIndexTools
{
    /// <summary>
    /// Represents a content item from either the database or the index, with all relevant metadata for comparison.
    /// </summary>
    public class ContentItem
    {
        public int NodeId { get; set; }
        public int VersionId { get; set; }
        public string Path { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public long TimestampNumeric { get; set; } // Numeric timestamp value (bigint from SQL)
        public long VersionTimestampNumeric { get; set; } // Numeric version timestamp value (bigint from SQL)
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
                if (!InDatabase) return "Index Only";
                if (!InIndex) return "DB Only";

                bool idsMatch = string.Equals(NodeId.ToString(), IndexNodeId) &&
                               string.Equals(VersionId.ToString(), IndexVersionId);

                // For timestamp comparison
                bool timestampMatch = false;
                if (!string.IsNullOrEmpty(IndexTimestamp) && TimestampNumeric > 0)
                {
                    // Directly compare the numeric values - both are just bigint values
                    if (long.TryParse(IndexTimestamp, out long indexTimestampNumeric))
                    {
                        // Direct comparison of bigint values
                        timestampMatch = (indexTimestampNumeric == TimestampNumeric);

                        // Log the comparison if verbose logging is enabled
                        if (ContentComparer.VerboseLogging && idsMatch && !timestampMatch)
                        {
                            Console.WriteLine($"TIMESTAMP DEBUG (NodeId={NodeId}): DB={TimestampNumeric}, Index={indexTimestampNumeric}, Match={timestampMatch}");
                        }
                    }
                }
                else if (idsMatch)
                {
                    // If IDs match but we can't compare timestamps (missing or invalid), assume it's a match
                    // This prevents false negatives when IDs match but timestamp data is incomplete
                    timestampMatch = true;
                    if (ContentComparer.VerboseLogging)
                    {
                        Console.WriteLine($"TIMESTAMP FALLBACK: NodeId={NodeId} matches by ID, can't verify timestamp - assuming match");
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
                   $"{(InDatabase ? Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "-")}\t{(InDatabase ? TimestampNumeric.ToString() : "-")}\t" +
                   $"{(InIndex ? IndexNodeId : "-")}\t{(InIndex ? IndexVersionId : "-")}\t" +
                   $"{(InIndex ? IndexTimestamp : "-")}\t" +
                   $"{Path}\t{NodeType}\t{Status}";
        }
    }
}
