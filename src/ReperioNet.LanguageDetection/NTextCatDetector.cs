using NTextCat;
using ReperioNet.Abstractions;

namespace ReperioNet.LanguageDetection;

/// <summary>
/// <see cref="ILanguageDetector"/> backed by NTextCat's ranked n-gram language identifier with the
/// bundled Core14 profile (Danish, German, English, French, Italian, Japanese, Korean, Dutch,
/// Norwegian, Portuguese, Russian, Spanish, Swedish, Chinese).
/// </summary>
/// <remarks>
/// <para>Loading the language profile is comparatively expensive — create one detector and reuse it
/// for the lifetime of the index. <see cref="Detect"/> only reads the loaded model and is safe for
/// concurrent use.</para>
/// <para>Detection quality degrades on very short texts (single words); when in doubt combine the
/// detector with <c>ReperioOptions&lt;TMeta&gt;.DefaultLanguage</c> or explicit entry languages.</para>
/// </remarks>
public sealed class NTextCatDetector : ILanguageDetector
{
    /// <summary>Maps the ISO 639-2T/639-3 codes used by the Core14 profile to ISO 639-1.</summary>
    private static readonly Dictionary<string, string> IsoTwoLetterCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dan"] = "da",
        ["deu"] = "de",
        ["eng"] = "en",
        ["fra"] = "fr",
        ["ita"] = "it",
        ["jpn"] = "ja",
        ["kor"] = "ko",
        ["nld"] = "nl",
        ["nor"] = "no",
        ["por"] = "pt",
        ["rus"] = "ru",
        ["spa"] = "es",
        ["swe"] = "sv",
        ["zho"] = "zh",
    };

    private readonly RankedLanguageIdentifier _identifier;

    /// <summary>Creates a detector using the bundled Core14 profile next to the application binaries.</summary>
    public NTextCatDetector()
        : this(DefaultProfilePath())
    {
    }

    /// <summary>Creates a detector from an NTextCat language profile file.</summary>
    /// <param name="profilePath">Path of the profile XML (e.g. <c>Core14.profile.xml</c>).</param>
    public NTextCatDetector(string profilePath)
    {
        ArgumentNullException.ThrowIfNull(profilePath);
        _identifier = new RankedLanguageIdentifierFactory().Load(profilePath);
    }

    /// <summary>
    /// Returns the ISO 639-1 code of the most likely language of <paramref name="text"/>, or
    /// <see langword="null"/> when the text is blank, no candidate is found or the profile's code
    /// has no ISO 639-1 mapping.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    public string? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var mostCertain = _identifier.Identify(text).FirstOrDefault();
        if (mostCertain is null)
        {
            return null;
        }

        var profileCode = mostCertain.Item1.Iso639_2T;
        return profileCode is not null && IsoTwoLetterCodes.TryGetValue(profileCode, out var isoCode)
            ? isoCode
            : null;
    }

    private static string DefaultProfilePath()
        => Path.Combine(AppContext.BaseDirectory, "LanguageModels", "Core14.profile.xml");
}
