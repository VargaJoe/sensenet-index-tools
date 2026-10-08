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
        public int? IndexDocumentId { get; set; }

        public string Status
        {
            get
            {
                if (!InDatabase) return "Index only";
                if (!InIndex) return "DB only";

                bool idsMatch = string.Equals(NodeId.ToString(), IndexNodeId) &&
                               string.Equals(VersionId.ToString(), IndexVersionId);

                if (!idsMatch) return "ID mismatch";
                if (TimestampNumeric <= 0 || VersionTimestampNumeric <= 0 ||
                    !long.TryParse(IndexTimestamp, out var nodeTimestamp) ||
                    !long.TryParse(IndexVersionTimestamp, out var versionTimestamp))
                    return "Timestamp unavailable";
                return nodeTimestamp == TimestampNumeric && versionTimestamp == VersionTimestampNumeric
                    ? "Match" : "Timestamp mismatch";

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
