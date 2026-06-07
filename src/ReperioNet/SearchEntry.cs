namespace ReperioNet;

/// <summary>A document to index: text content plus a strongly-typed metadata payload.</summary>
/// <typeparam name="TMeta">The metadata type returned with search hits.</typeparam>
/// <param name="Id">Required, non-empty, caller-stable identifier (e.g. a file path or GUID).</param>
/// <param name="Content">The text to index (may be empty).</param>
/// <param name="Metadata">The metadata payload returned with hits.</param>
/// <param name="Language">Optional explicit ISO 639-1 language code; <see langword="null"/> defers to the detector or default language.</param>
public sealed record SearchEntry<TMeta>(
    string Id,
    string Content,
    TMeta Metadata,
    string? Language = null);
