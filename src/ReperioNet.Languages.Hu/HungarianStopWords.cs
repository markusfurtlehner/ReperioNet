using ReperioNet.Abstractions;

namespace ReperioNet.Languages.Hu;

/// <summary>
/// Hungarian stop-word filter; a curated subset of the official Snowball Hungarian stop-word list
/// (articles, conjunctions, pronouns and common function words).
/// </summary>
public sealed class HungarianStopWords : IStopWordFilter
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "a", "ahogy", "ahol", "aki", "akik", "akkor", "alatt", "által", "általában",
        "amely", "amelyek", "amelyet", "amelynek", "ami", "amit", "amíg", "amikor",
        "át", "abban", "ahhoz", "annak", "arra", "arról", "az", "azok", "azon",
        "azt", "azzal", "azért", "aztán", "azután", "azonban", "bár", "be", "belül",
        "benne", "csak", "de", "e", "eddig", "egész", "egy", "egyes", "egyéb",
        "egyik", "egyre", "ekkor", "el", "elég", "ellen", "elő", "először", "előtt",
        "én", "éppen", "ebben", "ehhez", "ennek", "erre", "ez", "ezt", "ezek",
        "ezen", "ezzel", "ezért", "és", "fel", "felé", "hanem", "hiszen", "hogy",
        "hogyan", "igen", "így", "illetve", "ilyen", "ismét", "itt", "kell",
        "kellett", "keresztül", "ki", "kívül", "között", "közül", "legalább",
        "lehet", "legyen", "lenne", "lenni", "lesz", "lett", "maga", "magát",
        "majd", "már", "más", "másik", "meg", "még", "mellett", "mert", "mely",
        "melyek", "mi", "mit", "míg", "miért", "milyen", "mikor", "minden",
        "mindent", "mindenki", "mindig", "mint", "mintha", "mivel", "most", "ne",
        "néha", "nekem", "neki", "nem", "néhány", "nélkül", "nincs", "olyan",
        "ott", "össze", "ő", "ők", "őket", "pedig", "persze", "rá", "s", "saját",
        "sem", "semmi", "számára", "szemben", "szerint", "szinte", "talán",
        "tehát", "tovább", "továbbá", "több", "úgy", "ugyanis", "után", "utána",
        "vagy", "vagyis", "valaki", "valami", "valamint", "való", "vagyok", "van",
        "vannak", "volt", "voltam", "voltak", "voltunk", "vissza", "vele",
        "viszont", "volna",
    };

    /// <inheritdoc />
    public bool IsStopWord(string token) => Words.Contains(token);
}
