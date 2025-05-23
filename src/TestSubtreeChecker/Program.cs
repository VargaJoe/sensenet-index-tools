using System.Data;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SenseNetIndexTools;
using Directory = Lucene.Net.Store.Directory;
using IODirectory = System.IO.Directory;

namespace TestSubtreeChecker
{
    /// <summary>
    /// Simple test program to verify the enhanced search functionality in the SubtreeIndexChecker
    /// </summary>
    public class SubtreeCheckerTester
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Enhanced SubtreeIndexChecker Test");
            Console.WriteLine("=================================");
            
            // Create a temporary index with test data
            var tempDir = Path.Combine(Path.GetTempPath(), $"SN_TestIndex_{Guid.NewGuid()}");
            IODirectory.CreateDirectory(tempDir);
            
            try
            {
                // Create test index
                Console.WriteLine($"Creating test index in {tempDir}");
                CreateTestIndex(tempDir);
                
                // Now test the search methods with various cases
                TestIndexSearching(tempDir);
                
                Console.WriteLine("Test completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during test: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                // Clean up
                try
                {
                    IODirectory.Delete(tempDir, true);
                    Console.WriteLine($"Cleaned up test directory: {tempDir}");
                }
                catch
                {
                    Console.WriteLine($"Failed to clean up test directory: {tempDir}");
                }
            }
        }
        
        /// <summary>
        /// Creates a test index with various documents using different indexing strategies
        /// </summary>
        static void CreateTestIndex(string indexPath)
        {
            using (Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(indexPath)))
            {
                var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_29);
                using (var writer = new IndexWriter(dir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED))
                {
                    // Create several documents with different indexing patterns
                      // Document 1: Basic document with Id and Version_
                    var doc1 = new Document();
                    doc1.Add(new Field("Id", NumericUtils.IntToPrefixCoded(101), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc1.Add(new Field("Version_", NumericUtils.IntToPrefixCoded(1001), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc1.Add(new Field("Path", "/Root/Test/Document1", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    writer.AddDocument(doc1);
                    
                    // Document 2: Only has Id, not Version_
                    var doc2 = new Document();
                    doc2.Add(new Field("Id", NumericUtils.IntToPrefixCoded(102), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc2.Add(new Field("Path", "/Root/Test/Document2", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    writer.AddDocument(doc2);
                    
                    // Document 3: Has path in lowercase (common in SenseNet)
                    var doc3 = new Document();
                    doc3.Add(new Field("Id", NumericUtils.IntToPrefixCoded(103), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc3.Add(new Field("Version_", NumericUtils.IntToPrefixCoded(1003), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc3.Add(new Field("Path", "/root/test/document3", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    writer.AddDocument(doc3);
                    
                    // Document 4: Uses InFolder and Name fields
                    var doc4 = new Document();
                    doc4.Add(new Field("Id", NumericUtils.IntToPrefixCoded(104), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc4.Add(new Field("Version_", NumericUtils.IntToPrefixCoded(1004), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc4.Add(new Field("Name", "document4", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc4.Add(new Field("InFolder", "/root/test", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    writer.AddDocument(doc4);
                    
                    // Document 5: Uses InTree field
                    var doc5 = new Document();
                    doc5.Add(new Field("Id", NumericUtils.IntToPrefixCoded(105), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc5.Add(new Field("Version_", NumericUtils.IntToPrefixCoded(1005), Field.Store.YES, Field.Index.NOT_ANALYZED));
                    doc5.Add(new Field("InTree", "/root/test", Field.Store.YES, Field.Index.NOT_ANALYZED));
                    writer.AddDocument(doc5);
                    
                    // Document 6: Not found - for negative testing
                    
                    writer.Optimize();
                    writer.Commit();
                }
            }
            
            Console.WriteLine("Created test index with 5 documents using different indexing patterns");
        }
        
        /// <summary>
        /// Tests our enhanced search functionality with the test index
        /// </summary>
        static void TestIndexSearching(string indexPath)
        {
            using (Directory dir = FSDirectory.Open(new System.IO.DirectoryInfo(indexPath)))
            {
                using (var reader = IndexReader.Open(dir, true))
                {
                    // Test our CheckItemInIndex method with different scenarios
                    Console.WriteLine("\nTesting search strategies:");
                    
                    // Use reflection to access the private method
                    var methodInfo = typeof(SubtreeIndexChecker).GetMethod("CheckItemInIndex", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    
                    if (methodInfo == null)
                    {
                        Console.WriteLine("ERROR: Could not access CheckItemInIndex method via reflection");
                        return;
                    }
                    
                    // Test 1: Basic ID search
                    TestSearch(methodInfo, reader, 101, 1001, "/Root/Test/Document1", true, "Basic ID search");
                    
                    // Test 2: NodeId fallback
                    TestSearch(methodInfo, reader, 102, 9999, "/Root/Test/Document2", true, "NodeId fallback");
                    
                    // Test 3: Case sensitivity in path
                    TestSearch(methodInfo, reader, 103, 9999, "/Root/Test/Document3", true, "Lowercase path");
                    
                    // Test 4: InFolder + Name
                    TestSearch(methodInfo, reader, 104, 9999, "/Root/Test/Document4", true, "InFolder + Name");
                    
                    // Test 5: InTree
                    TestSearch(methodInfo, reader, 105, 9999, "/Root/Test/Document5", true, "InTree");
                    
                    // Test 6: Negative test - should not find anything
                    TestSearch(methodInfo, reader, 999, 9999, "/Root/Test/DoesNotExist", false, "Negative test");
                }
            }
        }
        
        static void TestSearch(System.Reflection.MethodInfo method, IndexReader reader, int nodeId, int versionId, string path, bool expectedResult, string testName)
        {
            try
            {
                var result = method.Invoke(null, new object[] { reader, nodeId, versionId, path });
                bool success = (bool)result == expectedResult;
                
                Console.WriteLine($"  {testName}: {(success ? "PASSED" : "FAILED")} (expected: {expectedResult}, got: {result})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {testName}: ERROR - {ex.Message}");
            }
        }
    }
}
