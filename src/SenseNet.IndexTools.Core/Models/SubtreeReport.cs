using System;
using System.Collections.Generic;
using System.Linq;

namespace SenseNet.IndexTools.Core.Models
{
    /// <summary>
    /// Report model for comparing content between database and index in a subtree.
    /// </summary>
    public class SubtreeReport
    {
        /// <summary>
        /// Path in the content repository that was checked.
        /// </summary>
        public string RepositoryPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether the check was performed recursively on child items.
        /// </summary>
        public bool Recursive { get; set; }

        /// <summary>
        /// When the check started.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the check completed.
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Total number of items found in the database.
        /// </summary>
        public int DatabaseItemsCount { get; set; }

        /// <summary>
        /// Total number of documents found in the index.
        /// </summary>
        public int IndexDocCount { get; set; }

        /// <summary>
        /// Number of items that matched perfectly between database and index.
        /// </summary>
        public int MatchedItemsCount { get; set; }

        /// <summary>
        /// List of items that did not match between database and index.
        /// </summary>
        public List<ContentItem> MismatchedItems { get; set; } = new List<ContentItem>();

        /// <summary>
        /// List of items that matched perfectly between database and index.
        /// </summary>
        public List<ContentItem> MatchedItems { get; set; } = new List<ContentItem>();

        /// <summary>
        /// Statistics about content types found during the check.
        /// </summary>
        public Dictionary<string, int> ContentTypeStats { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Number of mismatches per content type.
        /// </summary>
        public Dictionary<string, int> MismatchesByType { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Calculated match rate as a percentage.
        /// </summary>
        public double MatchRate => DatabaseItemsCount > 0 
            ? (MatchedItemsCount * 100.0 / DatabaseItemsCount) 
            : 0;
    }
}
