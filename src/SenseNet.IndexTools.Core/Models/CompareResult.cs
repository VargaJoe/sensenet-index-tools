using System;
using System.Collections.Generic;

namespace SenseNet.IndexTools.Core.Models
{
    public class CompareResult
    {
        public string IndexPath { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public bool Recursive { get; set; }
        public int Depth { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime EndTime { get; set; } = DateTime.Now;
        public int DbItemCount { get; set; }
        public int IndexItemCount { get; set; }
        public int MatchedItemCount { get; set; }
        public int MismatchedItemCount { get; set; }
        public List<ContentItem> MismatchedItems { get; set; } = new List<ContentItem>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }    // Using the main ContentItem class from Models/ContentItem.cs
}
