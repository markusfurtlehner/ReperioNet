using ReperioNet.Languages.Da;
using ReperioNet.Languages.De;
using ReperioNet.Languages.En;
using ReperioNet.Languages.Es;
using ReperioNet.Languages.Fi;
using ReperioNet.Languages.Fr;
using ReperioNet.Languages.Hu;
using ReperioNet.Languages.It;
using ReperioNet.Languages.Nl;
using ReperioNet.Languages.No;
using ReperioNet.Languages.Pt;
using ReperioNet.Languages.Ro;
using ReperioNet.Languages.Ru;
using ReperioNet.Languages.Sv;
using ReperioNet.Languages.Tr;

namespace ReperioNet.Languages.All;

/// <summary>Registers every ReperioNet European language pack in one call.</summary>
public static class AllLanguagesExtensions
{
    /// <summary>
    /// Registers all fifteen European language analyzers — German, English, French, Spanish,
    /// Italian, Portuguese, Dutch, Swedish, Norwegian, Danish, Finnish, Russian, Hungarian,
    /// Romanian and Turkish (PRD §3) — on <paramref name="o"/>.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type stored with each document.</typeparam>
    /// <param name="o">The options to register the analyzers on.</param>
    /// <returns><paramref name="o"/>, for chaining.</returns>
    public static ReperioOptions<TMeta> AddAllEuropeanLanguages<TMeta>(this ReperioOptions<TMeta> o)
        => o.AddGerman().AddEnglish().AddFrench().AddSpanish().AddItalian().AddPortuguese()
            .AddDutch().AddSwedish().AddNorwegian().AddDanish().AddFinnish().AddRussian()
            .AddHungarian().AddRomanian().AddTurkish();
}
