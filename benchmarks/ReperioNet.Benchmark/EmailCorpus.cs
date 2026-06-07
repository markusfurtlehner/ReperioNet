using System.Text;
using System.Text.Json.Serialization;

namespace ReperioNet.Benchmark;

/// <summary>Metadata payload stored with every benchmark document.</summary>
public sealed record EmailMeta(string Subject, string From);

/// <summary>Source-generated JSON context (required by ReperioNet).</summary>
[JsonSerializable(typeof(EmailMeta))]
public sealed partial class BenchmarkJsonContext : JsonSerializerContext;

/// <summary>
/// Deterministic generator of email-like documents (~60-160 body words) in en/de/fr (5:3:2).
/// Every document carries a unique token ("uidNNNNNNN") so needle queries have exactly one answer.
/// </summary>
public static class EmailCorpus
{
    private static readonly string[] EnWords =
    [
        "invoice", "payment", "meeting", "schedule", "project", "review", "report", "update",
        "deadline", "contract", "offer", "delivery", "shipment", "order", "customer", "account",
        "monthly", "quarterly", "budget", "approval", "request", "attached", "document", "details",
        "confirm", "received", "regards", "thanks", "please", "kindly", "follow", "discussion",
        "team", "office", "support", "issue", "ticket", "release", "version", "feature",
    ];

    private static readonly string[] DeWords =
    [
        "rechnung", "rechnungen", "zahlung", "besprechung", "termin", "projekt", "bericht",
        "vertrag", "angebot", "lieferung", "bestellung", "kunde", "konto", "monatlich",
        "freigabe", "anfrage", "anbei", "unterlagen", "einzelheiten", "bestätigung", "erhalten",
        "grüße", "danke", "bitte", "rückmeldung", "abteilung", "büro", "anhang", "prüfung",
        "frist", "mahnung", "betrag", "überweisung", "steuern", "buchhaltung", "versand",
    ];

    private static readonly string[] FrWords =
    [
        "facture", "paiement", "réunion", "calendrier", "projet", "rapport", "contrat",
        "offre", "livraison", "commande", "client", "compte", "mensuel", "approbation",
        "demande", "document", "détails", "confirmation", "reçu", "cordialement", "merci",
        "veuillez", "suivi", "équipe", "bureau", "dossier", "vérification", "montant",
        "chevaux", "campagne", "délai", "relance", "virement", "comptabilité", "expédition",
    ];

    private static readonly string[] Names =
    [
        "Müller", "Mueller", "Meier", "Maier", "Schmidt", "Schmitt", "Smith", "Johnson",
        "Dupont", "Martin", "Garcia", "Rossi", "Jansen", "Lindgren", "Hansen", "Korhonen",
    ];

    /// <summary>The language assigned to document <paramref name="i"/> (en 50%, de 30%, fr 20%).</summary>
    public static string LanguageOf(long i) => (i % 10) switch
    {
        < 5 => "en",
        < 8 => "de",
        _ => "fr",
    };

    /// <summary>The unique needle token embedded in document <paramref name="i"/>.</summary>
    public static string UniqueToken(long i) => $"uid{i:D7}";

    /// <summary>Builds document <paramref name="i"/> deterministically.</summary>
    public static SearchEntry<EmailMeta> Create(long i)
    {
        var random = new Random(unchecked((int)(0x5EED + i * 2654435761)));
        var language = LanguageOf(i);
        var words = language switch { "de" => DeWords, "fr" => FrWords, _ => EnWords };

        var name = Names[random.Next(Names.Length)];
        var subject = Compose(random, words, random.Next(4, 8));

        var body = new StringBuilder(1024);
        body.Append(language switch
        {
            "de" => $"Sehr geehrter Herr {name},",
            "fr" => $"Bonjour {name},",
            _ => $"Dear Mr {name},",
        });
        body.Append('\n');

        var sentences = random.Next(5, 13);
        for (var s = 0; s < sentences; s++)
        {
            body.Append(Compose(random, words, random.Next(8, 15)));
            if (s == 1)
            {
                // A reference number in most mails, like real invoices/tickets.
                body.Append(language == "de" ? " Rechnung Nr. " : " ref no. ");
                body.Append(2020 + random.Next(7)).Append('-').Append(random.Next(10000, 99999));
            }

            body.Append(". ");
        }

        body.Append(UniqueToken(i));
        body.Append(language switch
        {
            "de" => "\nMit freundlichen Grüßen",
            "fr" => "\nCordialement",
            _ => "\nBest regards",
        });

        return new SearchEntry<EmailMeta>(
            $"mail-{i:D7}",
            body.ToString(),
            new EmailMeta(subject, $"{name.ToLowerInvariant()}@example.com"),
            language);
    }

    /// <summary>Lazily enumerates documents [start, start+count).</summary>
    public static IEnumerable<SearchEntry<EmailMeta>> Range(long start, long count)
    {
        for (var i = start; i < start + count; i++)
        {
            yield return Create(i);
        }
    }

    private static string Compose(Random random, string[] words, int count)
    {
        var builder = new StringBuilder(count * 9);
        for (var w = 0; w < count; w++)
        {
            if (w > 0)
            {
                builder.Append(' ');
            }

            builder.Append(words[random.Next(words.Length)]);
        }

        return builder.ToString();
    }
}
