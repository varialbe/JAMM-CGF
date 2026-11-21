using Jamm.Core.Merging;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;

namespace Jamm.Core.Building;

internal static class HarborRuntime
{
    internal const string Controller = "jmk_harbor_town_object_harbor";
    private const string Script = "jmk_harbor_town_script_";

    internal static void Apply(UndertaleData data, IReadOnlyDictionary<string, CodeText> baseline,
        Dictionary<string, string> recompile, Dictionary<string, string> generated, List<string> decisions)
    {
        if (data.GameObjects.ByName(Controller) is null) return;

        // The native events have already selected and opened the correct slot, including the legacy fallback.
        const string load = "gml_Object_SaveManager_Other_13";
        string loadBody = Get(load);
        int close = loadBody.LastIndexOf("ini_close();", StringComparison.Ordinal);
        if (close < 0) throw Missing(load);
        Put(load, loadBody.Insert(close, $"with ({Controller}) {Script}save(false);\n"));

        const string saveAnchor = "ini_close();\n    ini_open(\"meta.ini\");";
        foreach (int slotEvent in new[] { 14, 15 })
        {
            string name = $"gml_Object_SaveManager_Other_{slotEvent}";
            Replace(name, saveAnchor, $"with ({Controller}) {Script}save(true);\n    " + saveAnchor);
        }

        const string wallet = "gml_Object_Wallet_Draw_0";
        const string vanillaFormat = "number_to_dollar_string(min(floor(dollars), 999999999))";
        if (Get(wallet).Contains(vanillaFormat, StringComparison.Ordinal))
            Replace(wallet, vanillaFormat, Script + "format(dollars)");

        // The rod's mouse and gamepad events share these two handlers.
        foreach (int rodEvent in new[] { 15, 16 })
        {
            string name = $"gml_Object_Rod_Other_{rodEvent}";
            Put(name, $"if (instance_exists({Controller}) && {Controller}.harbor_menu) exit;\n" + Get(name));
        }

        if (data.GameObjects.ByName(ConsoleMod.ObjectName) is not null)
        {
            const string console = "gml_Object_obj_jamm_console_Step_1";
            const string help = "ds_list_add(jamm_console_log, \"  help        : list commands\");";
            Replace(console, help, help + "\n    ds_list_add(jamm_console_log, \"  harbor      : list or set harbor levels and give money\");");
            const string clear = "else if (jamm_cmd == \"clear\")";
            Replace(console, clear, $$"""
                else if (jamm_cmd == "harbor" || string_pos("harbor ", jamm_cmd) == 1)
                {
                    ds_list_add(jamm_console_log, {{Script}}command(string_delete(jamm_cmd, 1, 7)));
                }
                {{clear}}
                """);
        }
        decisions.Add("Harbor Town: linked dock interaction, the wallet display, and main and backup save slots.");

        string Get(string name) => recompile.GetValueOrDefault(name) ?? baseline.GetValueOrDefault(name)?.Text ?? throw Missing(name);

        void Replace(string name, string anchor, string replacement)
        {
            string body = Get(name);
            int at = body.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0 || body.IndexOf(anchor, at + anchor.Length, StringComparison.Ordinal) >= 0)
                throw Missing(name);
            Put(name, body.Remove(at, anchor.Length).Insert(at, replacement));
        }

        void Put(string name, string source) => generated[name] = recompile[name] = source;

        static InvalidDataException Missing(string name) =>
            new($"Harbor Town could not link '{name}'. Build with the August 16 legacy data.win and disable mods that replace this event.");
    }
}
