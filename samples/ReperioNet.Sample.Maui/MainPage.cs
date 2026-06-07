using System.Text.Json.Serialization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ReperioNet.Languages.All;

namespace ReperioNet.Sample.Maui;

/// <summary>Sample metadata payload stored with every indexed document.</summary>
public sealed record DocMeta(string Title);

/// <summary>Source-generated JSON context — required by ReperioNet (AOT/trimming-safe).</summary>
[JsonSerializable(typeof(DocMeta))]
public sealed partial class SampleJsonContext : JsonSerializerContext;

/// <summary>
/// Minimal on-device search demo: a multilingual index in the app data directory, one query box.
/// Exercises the full ReperioNet pipeline (SQLite FTS5, stemming, phonetic, trigram, fuzzy) under
/// iOS full AOT / Android trimming.
/// </summary>
public class MainPage : ContentPage
{
    private readonly Entry _query = new() { Placeholder = "Try: Rechnung, Mueller, run, cheval, galop" };
    private readonly Label _results = new() { Margin = new Thickness(0, 12, 0, 0) };
    private SearchIndex<DocMeta>? _index;

    /// <summary>Builds the page UI.</summary>
    public MainPage()
    {
        Title = "ReperioNet";

        var search = new Button { Text = "Search" };
        search.Clicked += async (_, _) => await RunSearchAsync();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 8,
                Children = { _query, search, _results },
            },
        };
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_index is not null)
        {
            return;
        }

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "reperionet-sample.db");
        _index = await SearchIndex<DocMeta>.OpenAsync(databasePath, o =>
        {
            o.MetadataTypeInfo = SampleJsonContext.Default.DocMeta;
            o.AddAllEuropeanLanguages();
            o.DefaultLanguage = "en";
        });

        await _index.AddRangeAsync(
        [
            new SearchEntry<DocMeta>("de-1", "Die Rechnungen von Herrn Müller sind angekommen.", new DocMeta("Rechnungseingang"), "de"),
            new SearchEntry<DocMeta>("en-1", "The quarterly invoices were checked while running the audit.", new DocMeta("Quarterly audit"), "en"),
            new SearchEntry<DocMeta>("fr-1", "Les chevaux galopent à travers la campagne française.", new DocMeta("Chevaux au galop"), "fr"),
        ]);

        _results.Text = $"Indexed {await _index.CountAsync()} documents — ready.";
    }

    private async Task RunSearchAsync()
    {
        if (_index is null || string.IsNullOrWhiteSpace(_query.Text))
        {
            return;
        }

        var hits = await _index.SearchAsync(_query.Text, new SearchQueryOptions { IncludeSnippet = true });
        _results.Text = hits.Count == 0
            ? "No hits."
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                hits.Select(h => $"{h.Score:F2}  {h.Metadata.Title}{Environment.NewLine}{h.Snippet}"));
    }
}
