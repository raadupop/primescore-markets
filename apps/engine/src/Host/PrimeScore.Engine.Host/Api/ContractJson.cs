using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PrimeScore.Engine.Host.Api;

/// <summary>
/// Request bodies must carry every field the contract requires. The generator maps a required
/// number, flag or enum to a non-nullable property (optional ones are nullable), which would
/// silently read an absent field as its default (a missing <c>aggregation</c> as
/// <c>WEIGHTED_MEAN</c>). Marking those properties required turns an absent field into a 400.
/// </summary>
internal static class ContractJson
{
    public static void RequireContractValueTypes(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(Require);
    }

    private static void Require(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || info.Type.Assembly != typeof(PrimeScore.Api.Contracts.ApiJson).Assembly)
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                property.IsRequired = true;
            }
        }
    }
}
