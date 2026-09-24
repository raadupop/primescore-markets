using System.Text.Json;
using PrimeScore.Api.Contracts;
using YamlDotNet.RepresentationModel;

namespace PrimeScore.Structural;

/// <summary>
/// Pins the one hand-written contract type to the contract, and keeps the black-box suite
/// black-box (SRS EVO-001a).
/// </summary>
public sealed class ContractFidelityTests
{
    [Fact]
    public void Hand_written_SignalInput_writes_exactly_the_contract_properties()
    {
        var schema = Schema("SignalInput");
        var full = new SignalInput
        {
            Source_category = SourceCategory.MARKET_DATA,
            Source_identifier = "fred:VIXCLS",
            Timestamp = new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero),
            Payload_type = PayloadType.STRUCTURED,
            Structured_payload = new MarketDataPayload { Instrument = "VIX", Value = 37.32 },
            Unstructured_payload = new UnstructuredPayload { Text = "text", Language = "en" },
        };
        var minimal = new SignalInput { Source_identifier = "fred:VIXCLS" };

        Assert.Equal(schema.Properties.Order(), WrittenProperties(full).Order());
        Assert.Subset(WrittenProperties(minimal).ToHashSet(), schema.Required.ToHashSet());
    }

    [Fact]
    public void Hand_written_SignalInput_round_trips_and_keeps_the_raw_element()
    {
        const string json = """{"source_category":"MACROECONOMIC","source_identifier":"fred:CPIAUCSL","timestamp":"2022-07-13T12:30:00+00:00","payload_type":"STRUCTURED","structured_payload":{"indicator_type":"INFLATION","region":"US","release_date":"2022-07-13T12:30:00+00:00"}}""";

        var signal = JsonSerializer.Deserialize<SignalInput>(json)!;

        Assert.Equal(SourceCategory.MACROECONOMIC, signal.Source_category);
        Assert.Equal("fred:CPIAUCSL", signal.Source_identifier);
        Assert.NotNull(signal.Raw);
        Assert.Equal("US", signal.Raw!.Value.GetProperty("structured_payload").GetProperty("region").GetString());
    }

    [Fact]
    public void Every_SignalInput_property_survives_a_write_and_read()
    {
        var original = new SignalInput
        {
            Source_category = SourceCategory.GEOPOLITICAL,
            Source_identifier = "curated:validation-set",
            Timestamp = new DateTimeOffset(2022, 2, 24, 3, 0, 0, TimeSpan.Zero),
            Payload_type = PayloadType.UNSTRUCTURED,
            Structured_payload = new GeopoliticalPayload { Event_region = "Europe", Event_type = "military_strike", Severity_estimate = 0.9, Actors = ["RU", "UA"] },
            Unstructured_payload = new UnstructuredPayload { Text = "Invasion reported.", Language = "en", Source_url = "https://example.org/report" },
        };

        var read = JsonSerializer.Deserialize<SignalInput>(JsonSerializer.Serialize(original))!;

        Assert.Equal(original.Source_category, read.Source_category);
        Assert.Equal(original.Source_identifier, read.Source_identifier);
        Assert.Equal(original.Timestamp, read.Timestamp);
        Assert.Equal(original.Payload_type, read.Payload_type);
        Assert.Equal("Europe", ((JsonElement)read.Structured_payload!).GetProperty("event_region").GetString());
        Assert.Equal("Invasion reported.", read.Unstructured_payload!.Text);
        Assert.Equal("en", read.Unstructured_payload.Language);
        Assert.Equal("https://example.org/report", read.Unstructured_payload.Source_url);
    }

    [Fact]
    public void Enum_values_serialize_as_their_contract_strings()
    {
        var options = new JsonSerializerOptions();
        ApiJson.Configure(options);

        Assert.Equal(">=", JsonDocument.Parse(JsonSerializer.Serialize(ConditionsOperator.Ge, options)).RootElement.GetString());
        var categories = JsonDocument.Parse(JsonSerializer.Serialize(new List<SourceCategory> { SourceCategory.CROSS_ASSET_FLOW }, options)).RootElement;
        Assert.Equal("CROSS_ASSET_FLOW", Assert.Single(categories.EnumerateArray()).GetString());
        Assert.Equal(ConditionsOperator.Ge, JsonSerializer.Deserialize<ConditionsOperator>("\">=\"", options));
    }

    [Fact]
    public void Acceptance_suite_references_only_the_generated_API_contract()
    {
        var project = EngineSolution.Projects["PrimeScore.Acceptance.Api"];

        var compiledAgainst = project.References.Where(reference => reference.ReferencesAssembly).Select(reference => reference.Name);
        Assert.Equal(["PrimeScore.Api.Contracts"], compiledAgainst);

        var assemblyPath = Path.Combine(Path.GetDirectoryName(project.Path)!, "bin", BuildConfiguration(), "net10.0", "PrimeScore.Acceptance.Api.dll");
        Assert.True(File.Exists(assemblyPath), $"Build the solution before the structural suite: {assemblyPath}");
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(assemblyPath);
        var engineAssemblies = module.AssemblyReferences
            .Select(reference => reference.Name)
            .Where(name => name.StartsWith("PrimeScore.", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(["PrimeScore.Api.Contracts"], engineAssemblies);
    }

    private static string BuildConfiguration() =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

    private static IEnumerable<string> WrittenProperties(SignalInput signal) =>
        JsonDocument.Parse(JsonSerializer.Serialize(signal)).RootElement.EnumerateObject().Select(property => property.Name);

    private static (IReadOnlyList<string> Properties, IReadOnlyList<string> Required) Schema(string name)
    {
        var contract = Path.GetFullPath(Path.Combine(EngineSolution.Root, "..", "..", "doc", "PrimeScore-API-v1.yaml"));
        var yaml = new YamlStream();
        using (var reader = new StreamReader(contract))
        {
            yaml.Load(reader);
        }

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var schema = (YamlMappingNode)((YamlMappingNode)((YamlMappingNode)root["components"])["schemas"])[name];
        var properties = ((YamlMappingNode)schema["properties"]).Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToArray();
        var required = ((YamlSequenceNode)schema["required"]).Children.Select(item => ((YamlScalarNode)item).Value!).ToArray();
        return (properties, required);
    }
}
