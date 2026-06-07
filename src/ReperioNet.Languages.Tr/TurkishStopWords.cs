using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Tr;

/// <summary>
/// Turkish stop-word filter; a curated list of common Turkish function words (pronouns,
/// conjunctions, postpositions, question particles and frequent auxiliaries).
/// </summary>
public sealed class TurkishStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "acaba", "ama", "ancak", "arada", "aslında", "ayrıca",
        "bana", "bazı", "belki", "ben", "benden", "beni", "benim", "beri",
        "bile", "bir", "birçok", "biri", "birkaç", "birşey",
        "biz", "bize", "bizden", "bizi", "bizim", "böyle", "böylece",
        "bu", "buna", "bunda", "bundan", "bunlar", "bunları", "bunların",
        "bunu", "bunun", "burada",
        "çok", "çünkü",
        "da", "daha", "dahi", "de", "defa", "değil", "diğer", "diye",
        "dolayı", "dolayısıyla",
        "eğer", "en",
        "fakat",
        "gibi", "göre",
        "hala", "hangi", "hatta", "hem", "henüz", "hep", "hepsi", "her",
        "herhangi", "herkes", "hiç", "hiçbir",
        "için", "ile", "ilgili", "ise", "işte", "itibaren", "itibariyle",
        "kadar", "karşın", "kendi", "kendine", "kendini", "kez", "ki",
        "kim", "kimden", "kime", "kimi", "kimse",
        "mı", "mi", "mu", "mü",
        "nasıl", "ne", "neden", "nedenle", "nerde", "nerede", "nereye",
        "niçin", "niye",
        "o", "olan", "olarak", "oldu", "olduğu", "olmak", "olması",
        "olsa", "olsun", "olup", "olur", "ona", "ondan", "onlar",
        "onlardan", "onları", "onların", "onu", "onun", "oysa", "öyle",
        "pek",
        "rağmen",
        "sadece", "sanki", "sen", "senden", "seni", "senin",
        "siz", "sizden", "sizi", "sizin",
        "şey", "şeyler", "şöyle", "şu", "şuna", "şunu",
        "tarafından", "tüm",
        "üzere",
        "var", "vardı", "ve", "veya",
        "ya", "yani", "yine", "yoksa",
        "zaten",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => Words.Contains(token);
}
