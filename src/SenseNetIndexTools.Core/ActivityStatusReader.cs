using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;

namespace SenseNetIndexTools;

public static class ActivityStatusReader
{
    public static (int LastActivityId, int[] Gaps) Read(string path)
    {
        using var directory = FSDirectory.Open(new DirectoryInfo(IndexSnapshot.Resolve(path)));
        using var reader = IndexReader.Open(directory, true);
        var commitData = reader.GetCommitUserData();
        if (commitData.TryGetValue("LastActivityId", out var value) && int.TryParse(value, out var committed))
            return (committed, commitData.TryGetValue("Gaps", out var gaps) && !string.IsNullOrEmpty(gaps)
                ? gaps.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray() : []);
        using var searcher = new IndexSearcher(reader);
        var hits = searcher.Search(new TermQuery(new Term("$#COMMIT", "$#COMMIT")), 2);
        if (hits.TotalHits > 1) throw new InvalidOperationException("Multiple activity commit documents found.");
        if (hits.TotalHits == 1)
        {
            var data = searcher.Doc(hits.ScoreDocs[0].Doc).Get("$#DATA") ?? "";
            var parts = data.Split('|');
            if (int.TryParse(parts[0], out var id))
                return (id, parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(',').Select(int.Parse).ToArray() : []);
            throw new InvalidOperationException("Activity commit document is invalid.");
        }
        throw new InvalidOperationException("No LastActivityId found in index.");
    }
}
