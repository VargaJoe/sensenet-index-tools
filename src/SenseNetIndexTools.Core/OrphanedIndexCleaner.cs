using Lucene.Net.Index;
using Lucene.Net.Store;

namespace SenseNetIndexTools;

public static class OrphanedIndexCleaner
{
    public static List<ContentItem> FindCandidates(IEnumerable<ContentItem> comparison)
    {
        var items = comparison.ToList();
        var databaseVersions = items.Where(i => i.InDatabase)
            .Select(i => (i.NodeId.ToString(), i.VersionId.ToString())).ToHashSet();
        // A different path/type is a comparison mismatch, not proof the version is absent from SQL.
        return items.Where(i => !i.InDatabase && i.InIndex &&
            !databaseVersions.Contains((i.IndexNodeId ?? "", i.IndexVersionId ?? ""))).ToList();
    }

    /// <summary>Remove only the exact documents found by an offline comparison.</summary>
    public static int Remove(string indexPath, IEnumerable<ContentItem> orphanedItems)
    {
        var items = orphanedItems.ToArray();
        using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
        if (IndexWriter.IsLocked(directory))
            throw new InvalidOperationException("Index is locked. Stop the writer before cleanup.");
        using var reader = IndexReader.Open(directory, false);
        // Validate every target before changing anything. A changed index must be compared again.
        foreach (var item in items)
        {
            if (item.InDatabase || !item.InIndex || item.IndexDocumentId is not int id ||
                id < 0 || id >= reader.MaxDoc() || reader.IsDeleted(id))
                throw new InvalidOperationException("Cleanup requires exact documents from an offline comparison.");
            var document = reader.Document(id);
            if (document.Get("Path") != item.Path ||
                (document.Get("Id") ?? document.Get("NodeId") ?? "0") != item.IndexNodeId ||
                (document.Get("VersionId") ?? document.Get("Version_") ?? "0") != item.IndexVersionId)
                throw new InvalidOperationException("Index changed since comparison. Compare it again before cleanup.");
        }
        var ids = items.Select(i => i.IndexDocumentId!.Value).Distinct().ToArray();
        foreach (var id in ids) reader.DeleteDocument(id);
        reader.Commit();
        return ids.Length;
    }
}
