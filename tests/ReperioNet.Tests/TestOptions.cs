namespace ReperioNet.Tests;

internal static class TestOptions
{
    /// <summary>Configure callback that sets the required MetadataTypeInfo plus optional extras.</summary>
    public static Action<ReperioOptions<TestMeta>> Configure(Action<ReperioOptions<TestMeta>>? extra = null)
        => o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            extra?.Invoke(o);
        };

    public static Task<SearchIndex<TestMeta>> OpenAsync(TestDatabase db, Action<ReperioOptions<TestMeta>>? extra = null)
        => SearchIndex<TestMeta>.OpenAsync(db.Path, Configure(extra));

    public static SearchEntry<TestMeta> Entry(string id, string content, string? language = null)
        => new(id, content, new TestMeta($"name-{id}", id.Length), language);
}
