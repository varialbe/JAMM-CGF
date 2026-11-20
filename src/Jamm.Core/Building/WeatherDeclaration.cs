using System.Text.Json;
using Jamm.Core.Merging;
using Jamm.Core.ModKit;

namespace Jamm.Core.Building;

internal sealed record WeatherFish(string Object, ModKitSpawnBox Spawn);

// Metadata travels through standalone archives and cached patches without patching a vanilla event.
internal sealed record WeatherDeclaration(string Package, ModKitWeather Weather, IReadOnlyList<WeatherFish> Population)
{
    internal const string Prefix = "jamm_weather_def_";
    private const string Signature = "CGF_WEATHER_1:";

    internal static bool IsDeclaration(string name) => name.StartsWith("gml_Script_" + Prefix, StringComparison.Ordinal);

    internal static string Encode(WeatherDeclaration declaration) =>
        $"return \"{Signature}{Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(declaration))}\";";

    internal static WeatherDeclaration Read(string name, string? gml)
    {
        var token = GmlReferences.Tokens(gml ?? "").FirstOrDefault(t => t.Kind == 's' && t.Value.StartsWith(Signature, StringComparison.Ordinal));
        if (token?.Value is { } value)
        {
            try
            {
                if (JsonSerializer.Deserialize<WeatherDeclaration>(Convert.FromBase64String(value[Signature.Length..])) is { } declaration
                    && declaration is { Package.Length: > 0, Weather.Id.Length: > 0, Population: not null })
                    return declaration;
            }
            catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
            {
                throw Error(ex);
            }
        }
        throw Error();

        InvalidDataException Error(Exception? inner = null) =>
            new($"Weather declaration '{name}' is unreadable. Rebuild the ModKit package and regenerate its patch.", inner);
    }
}
