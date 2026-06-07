using Microsoft.Data.Sqlite;
using ReperioNet.Internal;
using Xunit;

namespace ReperioNet.Tests;

public class SqliteVersionCheckTests
{
    [Theory]
    [InlineData("3.43.0", true)]   // exact minimum
    [InlineData("3.43", true)]     // missing patch defaults to 0
    [InlineData("3.43.1", true)]
    [InlineData("3.44.0", true)]
    [InlineData("3.50.1", true)]
    [InlineData("4.0.0", true)]
    [InlineData("3.42.0", false)]
    [InlineData("3.42.99", false)]
    [InlineData("3.9.0", false)]   // numeric, not lexicographic, comparison
    [InlineData("2.999.999", false)]
    [InlineData("3", false)]       // 3.0.0 < 3.43.0
    public void IsSupported_ComparesNumerically(string version, bool expected)
    {
        Assert.Equal(expected, SqliteVersionCheck.IsSupported(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("3.43.0-beta")]
    [InlineData("3..43")]
    [InlineData("3.43.0.1")]
    [InlineData("-3.43.0")]
    public void IsSupported_RejectsUnparsableVersions(string? version)
    {
        Assert.False(SqliteVersionCheck.IsSupported(version));
    }

    [Fact]
    public void BundledEngine_MeetsMinimumVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = (string)command.ExecuteScalar()!;

        Assert.True(
            SqliteVersionCheck.IsSupported(version),
            $"Bundled SQLite engine reports {version}, below the required {SqliteVersionCheck.MinimumVersion}.");
    }
}
