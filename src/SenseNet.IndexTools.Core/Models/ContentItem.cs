namespace SenseNet.IndexTools.Core.Models
{
    /// <summary>
    /// Represents a content item that can exist in both database and index.
    /// </summary>
    public class ContentItem
    {
        /// <summary>
        /// Node ID from the database.
        /// </summary>
        public long NodeId { get; set; }

        /// <summary>
        /// Version ID from the database.
        /// </summary>
        public long VersionId { get; set; }

        /// <summary>
        /// Node ID found in the index.
        /// </summary>
        public string? IndexNodeId { get; set; }

        /// <summary>
        /// Version ID found in the index.
        /// </summary>
        public string? IndexVersionId { get; set; }

        /// <summary>
        /// Content path in the repository.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Content type name.
        /// </summary>
        public string? NodeType { get; set; }

        /// <summary>
        /// Whether the item exists in the database.
        /// </summary>
        public bool InDatabase { get; set; }

        /// <summary>
        /// Whether the item exists in the index.
        /// </summary>
        public bool InIndex { get; set; }

        /// <summary>
        /// Status of the comparison between database and index ("Match", "ID Mismatch", etc.).
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Returns a string representation of the content item.
        /// </summary>
        public override string ToString()
        {
            var dbNodeId = InDatabase ? NodeId.ToString() : "-";
            var dbVerID = InDatabase ? VersionId.ToString() : "-";
            var idxNodeId = InIndex ? IndexNodeId : "-";
            var idxVerID = InIndex ? IndexVersionId : "-";
            return $"{dbNodeId}\t{dbVerID}\t{idxNodeId}\t{idxVerID}\t{Path}\t{NodeType}\t{Status}";
        }
    }
}
