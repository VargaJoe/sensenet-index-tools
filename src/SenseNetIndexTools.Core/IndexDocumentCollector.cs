using Lucene.Net.Index;
using Lucene.Net.Search;

namespace SenseNetIndexTools;

/// <summary>Visits every matching document once, without a top-hits limit.</summary>
internal sealed class IndexDocumentCollector(Action<int> visit) : Collector
{
    private int _docBase;
    public override void SetScorer(Scorer scorer) { }
    public override void Collect(int doc) => visit(_docBase + doc);
    public override void SetNextReader(IndexReader reader, int docBase) => _docBase = docBase;
    public override bool AcceptsDocsOutOfOrder() => true;
}
