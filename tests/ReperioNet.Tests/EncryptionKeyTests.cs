using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ReperioNet.Tests;

/// <summary>On the stock e_sqlite3 build (no encryption) a key is refused before anything is written: the index never lands on disk in plaintext.</summary>
public class EncryptionKeyTests
{
    [Fact]
    public async Task A_key_on_a_build_without_encryption_is_refused_before_anything_is_written()
    {
        using var db = new TestDatabase();

        await Assert.ThrowsAsync<ReperioException>(() => SearchIndex<TestMeta>.OpenAsync(db.Path, o =>
        {
            o.MetadataTypeInfo = TestMetaJsonContext.Default.TestMeta;
            o.EncryptionKey = RandomNumberGenerator.GetBytes(32);
        }));

        // No SQLite header was written: the file is absent or empty.
        if (File.Exists(db.Path))
        {
            var bytes = File.ReadAllBytes(db.Path);
            Assert.False(bytes.Length >= 16 && Encoding.ASCII.GetString(bytes, 0, 16) == "SQLite format 3\0", "a plaintext index was written");
        }
    }
}
