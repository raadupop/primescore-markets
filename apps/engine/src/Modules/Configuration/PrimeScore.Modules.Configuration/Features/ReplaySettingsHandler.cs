using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Configuration.Settings;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Configuration.Features;

internal sealed class ResolveReplaySettingsHandler(IQueryHandler<GetSettingsVersion, SettingsVersion?> versions)
    : IQueryHandler<ResolveReplaySettings, ReplaySettingsResult>
{
    private static readonly JsonSerializerOptions Strict = StrictOptions();

    private static JsonSerializerOptions StrictOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties.Where(property => property.Set is not null && !property.IsSetNullable))
            {
                property.IsRequired = true;
            }
        });
        return new(CanonicalJson.SerializerOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            TypeInfoResolver = resolver,
        };
    }

    public async Task<ReplaySettingsResult> HandleAsync(ResolveReplaySettings query, CancellationToken cancellationToken)
    {
        var basis = await versions.HandleAsync(new GetSettingsVersion(query.Version), cancellationToken).ConfigureAwait(false);
        if (basis is null)
        {
            return new(null, ["configuration version does not exist"]);
        }

        try
        {
            var node = JsonNode.Parse(CanonicalJson.Serialize(basis.Settings))!.AsObject();
            if (query.OverridesJson is { } json && JsonNode.Parse(json) is { } patch)
            {
                Merge(node, patch.AsObject());
                if (patch.AsObject().Count > 0)
                {
                    node["calibration"] = "in-sample";
                }
            }

            var settings = node.Deserialize<EngineSettings>(Strict)!;
            var errors = SettingsValidator.Validate(settings);
            return new(errors.Count == 0 ? settings : null, errors);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return new(null, ["config_overrides: " + exception.Message]);
        }
    }

    // Objects merge recursively. Arrays replace. Contexts also accept an object keyed by existing context name.
    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is null)
            {
                throw new JsonException($"'{key}' cannot be null.");
            }

            if (key == "contexts" && value is JsonObject contexts && target[key] is JsonArray existing)
            {
                foreach (var (name, changes) in contexts)
                {
                    var context = existing.OfType<JsonObject>().SingleOrDefault(item => item["name"]?.GetValue<string>() == name)
                        ?? throw new JsonException($"Unknown context '{name}'.");
                    Merge(context, changes?.AsObject() ?? throw new JsonException($"Context '{name}' cannot be null."));
                }
            }
            else if (value is JsonObject child && target[key] is JsonObject current)
            {
                Merge(current, child);
            }
            else
            {
                RejectNullChildren(value);
                target[key] = value.DeepClone();
            }
        }
    }

    private static void RejectNullChildren(JsonNode node)
    {
        var children = node switch
        {
            JsonArray array => array.AsEnumerable(),
            JsonObject obj => obj.Select(pair => pair.Value),
            _ => [],
        };
        foreach (var child in children)
        {
            if (child is null)
            {
                throw new JsonException("Override values cannot contain null.");
            }

            RejectNullChildren(child);
        }
    }
}
