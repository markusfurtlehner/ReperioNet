namespace ReperioNet.Languages.Tests;

/// <summary>A fresh, isolated database path per test, deleted on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory;

    public TestDatabase()
    {
        _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "reperionet-lang-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "index.db");
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; temp cleanup must not fail tests.
        }
    }
}

public static class TestOptions
{
    /// <summary>Opens an index with the required MetadataTypeInfo plus optional extra configuration.</summary>
    public static Task<SearchIndex<TestMeta>> OpenAsync(TestDatabase db, Action<ReperioOptions<TestMeta>>? extra = null)
        => SearchIndex<TestMeta>.OpenAsync(db.Path, o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            extra?.Invoke(o);
        });

    public static SearchEntry<TestMeta> Entry(string id, string content, string? language = null)
        => new(id, content, new TestMeta(id), language);
}
