using System;
using System.Collections.Generic;

namespace SenseNetIndexTools.Models
{
    /// <summary>
    /// Report model for index structure and integrity validation.
    /// </summary>
    public class ValidationReport
    {
        /// <summary>
        /// Path to the Lucene index that was validated.
        /// </summary>
        public string IndexPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether detailed validation was performed.
        /// </summary>
        public bool DetailedValidation { get; set; }

        /// <summary>
        /// When the validation started.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// When the validation completed.
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Collection of validation results with different severities.
        /// </summary>
        public List<ValidationResult> Results { get; set; } = new List<ValidationResult>();

        /// <summary>
        /// Number of validation results with Error severity.
        /// </summary>
        public int ErrorCount => Results.Count(r => r.Severity == ValidationSeverity.Error);

        /// <summary>
        /// Number of validation results with Warning severity.
        /// </summary>
        public int WarningCount => Results.Count(r => r.Severity == ValidationSeverity.Warning);

        /// <summary>
        /// Number of validation results with Info severity.
        /// </summary>
        public int InfoCount => Results.Count(r => r.Severity == ValidationSeverity.Info);

        /// <summary>
        /// Whether the validation passed without errors.
        /// </summary>
        public bool Success => ErrorCount == 0;

        /// <summary>
        /// List of field names found in the index.
        /// </summary>
        public List<string> IndexFields { get; set; } = new List<string>();

        /// <summary>
        /// Collection of fields that were expected but not found.
        /// </summary>
        public List<string> MissingFields { get; set; } = new List<string>();
    }

    /// <summary>
    /// Individual validation check result.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// The severity level of the validation result.
        /// </summary>
        public ValidationSeverity Severity { get; set; }

        /// <summary>
        /// Main message describing the validation result.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Additional details about the validation result.
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Creates a new validation result.
        /// </summary>
        public ValidationResult(ValidationSeverity severity, string message, string details = "")
        {
            Severity = severity;
            Message = message;
            Details = details;
        }
    }

    /// <summary>
    /// Possible severity levels for validation results.
    /// </summary>
    public enum ValidationSeverity
    {
        /// <summary>
        /// Informational message about the validation.
        /// </summary>
        Info,

        /// <summary>
        /// Warning that should be reviewed but doesn't prevent operation.
        /// </summary>
        Warning,

        /// <summary>
        /// Error that indicates a serious problem requiring attention.
        /// </summary>
        Error
    }
}
