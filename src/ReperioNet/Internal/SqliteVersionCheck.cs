using System.Globalization;

namespace ReperioNet.Internal;

/// <summary>Parses and compares the result of <c>sqlite_version()</c> against the required minimum.</summary>
internal static class SqliteVersionCheck
{
    /// <summary>Minimum SQLite version (3.43.0 introduces <c>contentless_delete=1</c>).</summary>
    internal static readonly Version MinimumVersion = new(3, 43, 0);

    /// <summary>Returns <see langword="true"/> if <paramref name="versionText"/> parses and is at least <see cref="MinimumVersion"/>.</summary>
    internal static bool IsSupported(string? versionText)
        => TryParse(versionText, out var version) && version >= MinimumVersion;

    /// <summary>
    /// Parses an SQLite version string ("major.minor.patch"; missing trailing components default to 0).
    /// Returns <see langword="false"/> for anything that is not 1–3 dot-separated non-negative integers.
    /// </summary>
    internal static bool TryParse(string? versionText, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var parts = versionText.Trim().Split('.');
        if (parts.Length > 3)
        {
            return false;
        }

        var components = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out components[i]))
            {
                return false;
            }
        }

        version = new Version(components[0], components[1], components[2]);
        return true;
    }
}
