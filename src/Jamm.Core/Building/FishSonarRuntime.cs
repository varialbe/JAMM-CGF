using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

/// <summary>Installs the shared sonar draw-state initializer used by JAMM fish spawners.</summary>
internal static class FishSonarRuntime
{
    internal const string Script = "jamm_fish_sonar_init";
    internal const string Code = "gml_Script_" + Script;

    internal static void Ensure(UndertaleData data, Dictionary<string, string> recompile,
        Dictionary<string, string> generated)
    {
        if (data.Scripts.ByName(Script) is null)
            data.Scripts.Add(new UndertaleScript
            {
                Name = data.Strings.MakeString(Script),
                Code = UndertaleCode.CreateEmptyEntry(data, Code)
            });

        string source;
        using (var stream = typeof(FishSonarRuntime).Assembly.GetManifestResourceStream("Jamm.Worldgen." + Script)
            ?? throw new InvalidOperationException("Missing shared fish sonar initializer. Rebuild JAMM."))
        using (var reader = new StreamReader(stream))
            source = reader.ReadToEnd();

        recompile[Code] = source;
        generated[Code] = source;
    }
}
