using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ReperioNet.Encryption.Tests;

public sealed record Note(string Title);

[JsonSerializable(typeof(Note))]
public sealed partial class NoteJsonContext : JsonSerializerContext;

/// <summary>An index with <see cref="ReperioOptions{TMeta}.EncryptionKey"/> on SQLite3 Multiple Ciphers: encrypted on disk, readable only with its key.</summary>
public sealed class EncryptedIndexTests : IDisposable
{
    private const string Marker = "zebraquokkamarker";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "reperionet-enc-tests", Guid.NewGuid().ToString("N"));
    private string IndexPath => Path.Combine(_dir, "index.db");

    public EncryptedIndexTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private Task<SearchIndex<Note>> Open(byte[]? key) =>
        SearchIndex<Note>.OpenAsync(IndexPath, o =>
        {
            o.MetadataTypeInfo = NoteJsonContext.Default.Note;
            o.EncryptionKey = key;
        });

    private static async Task Fill(SearchIndex<Note> index)
    {
        for (var i = 0; i < 50; i++)
            await index.AddAsync(new SearchEntry<Note>("n" + i, $"Ward round {i} patient notes {(i == 7 ? Marker : "")}", new Note("t" + i)));
    }

    private bool FilesContain(string text)
    {
        var needle = Encoding.UTF8.GetBytes(text);
        foreach (var file in Directory.GetFiles(_dir))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (memory.ToArray().AsSpan().IndexOf(needle) >= 0) return true;
        }
        return false;
    }

    private string Header()
    {
        using var stream = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[16];
        stream.ReadExactly(head);
        return Encoding.ASCII.GetString(head);
    }

    [Fact]
    public async Task An_index_with_a_key_is_encrypted_on_disk_and_its_wal_too()
    {
        var key = NewKey();
        await using (var index = await Open(key))
        {
            await Fill(index);
            Assert.False(FilesContain(Marker), "the marker is readable in the index file or its WAL while it is open");
        }
        Assert.NotEqual("SQLite format 3\0", Header());
        Assert.False(FilesContain(Marker));
        Assert.False(FilesContain("patient notes"));
    }

    [Fact]
    public async Task It_reopens_with_the_same_key_and_searches_as_before()
    {
        var key = NewKey();
        await using (var index = await Open(key)) await Fill(index);

        await using var again = await Open(key);
        Assert.Equal(50, await again.CountAsync());
        Assert.True(await again.ContainsAsync("n7"));
        var hits = await again.SearchAsync(Marker);
        Assert.Equal("n7", Assert.Single(hits).Id);
    }

    [Fact]
    public async Task A_wrong_key_is_refused_with_a_reperio_exception()
    {
        await using (var index = await Open(NewKey())) await Fill(index);
        SqliteConnection.ClearAllPools();

        await Assert.ThrowsAsync<ReperioException>(() => Open(NewKey()));
    }

    [Fact]
    public async Task An_encrypted_index_does_not_open_without_its_key()
    {
        await using (var index = await Open(NewKey())) await Fill(index);
        SqliteConnection.ClearAllPools();

        await Assert.ThrowsAnyAsync<Exception>(() => Open(null));
    }

    [Fact]
    public async Task Without_a_key_the_index_stays_plain_on_this_build()
    {
        await using (var index = await Open(null)) await Fill(index);
        Assert.Equal("SQLite format 3\0", Header());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(33)]
    public async Task A_key_that_is_not_32_bytes_is_refused(int length)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Open(new byte[length]));
    }
}
