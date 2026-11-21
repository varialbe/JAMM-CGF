using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Bench scene for big money: shows $1.2B and $3.4T in the wallet and checks the INI real round trip.</summary>
internal static class MoneyScene
{
    private const string Probe = "obj_jamm_bench_money";

    internal static bool Requested(string[] args)
    {
        int index = Array.IndexOf(args, "--scene");
        return index >= 3 && args.ElementAtOrDefault(index + 1) == "money";
    }

    internal static void Apply(UndertaleData data)
    {
        if (data.Rooms.ByName("NewWorld") is null || data.GameObjects.ByName("Wallet") is null)
            throw new InvalidDataException("The money scene needs the legacy NewWorld room and Wallet object.");
        var probe = ModObjects.Create(data, Probe, persistent: false);
        var compiler = new CompileGroup(data);
        string dismiss = string.Join('\n', new[] { "GameModeMenu", "GM_Classic", "GM_Realism", "Message_Start" }
            .Where(name => data.GameObjects.ByName(name) is not null)
            .Select(name => $"with ({name}) instance_destroy();"));
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Create, 0u, data), """
            bench_start = -1;
            bench_stage = 0;
            """);
        compiler.QueueCodeReplace(probe.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.BeginStep, data), $$"""
            if (room != NewWorld || !instance_exists(SaveManager) || !SaveManager.loaded || !instance_exists(Girl)) exit;
            SaveManager.saving_locked = 1;
            if (bench_start < 0) bench_start = current_time;
            var t = current_time - bench_start;
            if (t < 4000) exit;
            {{dismiss}}
            Girl.startMessageShown = 1;
            Girl.hide_ui = 0;
            // The wallet is only shown while a shop is open.
            if (!instance_exists(Handler) && instance_exists(icon_Rods))
                instance_create(icon_Rods.ORIGIN_X, icon_Rods.ORIGIN_Y, RodMenu);
            if (bench_stage == 0)
            {
                Girl.dollars = 1234567890;
                bench_stage = 1;
            }
            else if (bench_stage == 1 && t > 25000)
            {
                Girl.dollars = 3400000 * 1000000;
                bench_stage = 2;
            }
            else if (bench_stage == 2 && t > 46000)
            {
                var big = 3400000 * 1000000 + 123;
                ini_open("jamm_money_test.ini");
                ini_write_real("t", "v", big);
                ini_close();
                ini_open("jamm_money_test.ini");
                var back = ini_read_real("t", "v", 0);
                ini_close();
                var log = file_text_open_write("jamm_bench_money.csv");
                file_text_write_string(log, "wallet,dollars,ini_in,ini_out,exact,format_big");
                file_text_writeln(log);
                var shown = "";
                if (instance_exists(Wallet) && variable_instance_exists(Wallet.id, "dollars_string")) shown = Wallet.dollars_string;
                file_text_write_string(log, shown + "," + string_format(Girl.dollars, 0, 0) + ","
                    + string_format(big, 0, 0) + "," + string_format(back, 0, 0) + "," + string(back == big) + ","
                    + number_to_dollar_string(big));
                file_text_writeln(log);
                file_text_close(log);
                game_end();
            }
            """);
        ModObjects.PlaceInstance(data, "NewWorld", probe, first: false);
        ModObjects.Compile(compiler);
    }
}
