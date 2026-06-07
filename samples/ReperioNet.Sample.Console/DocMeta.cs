using System.Text.Json.Serialization;

namespace ReperioNet.Sample.ConsoleApp;

/// <summary>Sample metadata payload stored with every indexed document.</summary>
public sealed record DocMeta(string Title, string Path);

/// <summary>Source-generated JSON context — required by ReperioNet (AOT/trimming-safe, no reflection).</summary>
[JsonSerializable(typeof(DocMeta))]
public sealed partial class SampleJsonContext : JsonSerializerContext;
