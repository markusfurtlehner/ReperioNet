namespace ReperioNet;

/// <summary>
/// A document and the part of its text a test looks at (2.7.1), for <c>FindContainingAmongAsync</c>.
/// </summary>
/// <param name="Id">The document's id.</param>
/// <param name="Start">The first character of the part (UTF-16 units, as in a .NET string); past the end means an empty part.</param>
/// <param name="Length">The length of the part; negative means to the end of the text. A part that runs past the end is cut there.</param>
public readonly record struct TextSlice(string Id, int Start = 0, int Length = -1);
