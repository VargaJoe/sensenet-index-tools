using System;
using System.Collections.Generic;

namespace SenseNetIndexTools.Models
{
    /// <summary>
    /// Report model for tracking last activity ID and gaps in activity sequence.
    /// </summary>
    public class LastActivityReport
    {
        /// <summary>
        /// The last processed activity ID from the index.
        /// </summary>
        public long LastActivityId { get; set; }

        /// <summary>
        /// Collection of activity IDs that represent gaps in the sequence.
        /// </summary>
        public IEnumerable<long>? ActivityGaps { get; set; }

        /// <summary>
        /// The path to the Lucene index being analyzed.
        /// </summary>
        public string? IndexPath { get; set; }

        /// <summary>
        /// Timestamp when the report was generated.
        /// </summary>
        public string? Timestamp { get; set; }

        /// <summary>
        /// Indicates if the activity check was successful.
        /// </summary>
        public bool? Success { get; set; }
    }
}
