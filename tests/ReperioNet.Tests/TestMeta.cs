using System.Text.Json.Serialization;

namespace ReperioNet.Tests;

public sealed record TestMeta(string Name, int Value);

[JsonSerializable(typeof(TestMeta))]
public sealed partial class TestMetaJsonContext : JsonSerializerContext;
