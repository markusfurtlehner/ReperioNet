using System.Text.Json.Serialization;

namespace ReperioNet.Languages.Tests;

public sealed record TestMeta(string Name);

[JsonSerializable(typeof(TestMeta))]
public sealed partial class TestMetaJsonContext : JsonSerializerContext;
