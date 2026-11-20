using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

namespace Jamm.ModMaker;

/// <summary>Installs the guarded Perf fish-neighbour optimization on a final GM1 archive.</summary>
public static partial class FishGrid
{
    /// <summary>Object that owns the grid and its tick counter.</summary>
    public const string Controller = "obj_jamm_fish_grid";
    private const string Avoid = "gml_Script_fish_avoid_fish";
    private const string EndStep = "gml_Object_Fish_Step_2";
    private const string VanillaPattern = "C3C709C80BC54143347ED06911CD3A55DBA1EAC112D122A7E7ED608DEF7B8804";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Applies the grid when the original avoidance loop and supported runner layout are present.</summary>
    public static string Apply(UndertaleData data) => Apply(data, []);

    internal static string Apply(UndertaleData data, List<UndertaleCode> edits)
    {
        if (data.GameObjects.ByName(Controller) is not null)
            return Decision("skipped (runtime already installed)");
        if (data.GeneralInfo.Major != 1 || data.GeneralInfo.BytecodeVersion != 16
            || data.Code.Any(code => !LegacyCodeHooks.Supports(code))
            || data.Rooms.ByName("NewWorld") is null || data.GameObjects.ByName("FishLarge") is not { } fishLarge
            || data.GameObjects.ByName("Fish") is not { } fish
            || data.Code.ByName(EndStep) is not { } endStep || data.Code.ByName(Avoid) is not { } avoid
            || !fish.Events[(int)EventType.Step].Any(ev => ev.EventSubtype == (uint)EventSubtypeStep.EndStep
                && ev.Actions.Any(action => action.CodeId == endStep)))
            return Decision("skipped (required legacy fish events or GameMaker bytecode layout missing)");

        if (data.Code.Any(MayPerformEndStep))
            return Decision("skipped (a script can invoke the End Step outside the runner's motion phase)");
        if (data.Code.SelectMany(code => code.Instructions).Any(instruction => instruction.Kind == Opcode.Call
            && instruction.ValueFunction?.Name.Content is "object_set_parent" or "object_event_add" or "object_event_clear"))
            return Decision("skipped (a script can change object ancestry or event bindings at runtime)");

        string source;
        try { source = ModObjects.Decompile(data, avoid); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Decision("skipped (avoidance script could not be decompiled)");
        }

        if (!Matches(source))
            return Decision("skipped (avoidance loop differs from the audited pattern)");
        if (FishGridScripts.Scripts.Keys.Concat(Lifecycle.Keys.Select(call => WrapperName(call.Name, call.Count)))
            .Any(name => data.Scripts.ByName(name) is not null || data.Code.ByName("gml_Script_" + name) is not null
                || data.Functions.ByName(name) is not null))
            return Decision("skipped (runtime script names already in use)");

        // Capture the game's entries before adding helper code. The instrumented game code
        // invalidates the snapshot before membership can change, even across nested events.
        var originals = data.Code.Where(code => code != avoid).ToArray();
        var compiler = new CompileGroup(data);
        foreach (var (name, script) in FishGridScripts.Scripts) AddScript(name, script);
        foreach (var (call, script) in Lifecycle) AddScript(WrapperName(call.Name, call.Count), script);
        var controller = ModObjects.Create(data, Controller, persistent: true);
        controller.Visible = false;
        AddEvent(EventType.Create, 0u, """
            if (instance_number(object_index) > 1) { instance_destroy(); exit; }
            ini_open("jamm_perf.ini");
            global.jamm_fg_enabled = ini_read_real("Perf", "FishGrid", 1) != 0;
            ini_close();
            global.jamm_fg_heads = ds_map_create();
            global.jamm_fg_orders = ds_list_create();
            global.jamm_fg_serial = 0;
            jamm_fg_owner = true;
            """ + "\n" + FishGridScripts.Reset);
        AddEvent(EventType.Destroy, 0u, """
            if (!variable_instance_exists(id, "jamm_fg_owner")) exit;
            global.jamm_fg_enabled = false;
            global.jamm_fg_valid = false;
            ds_map_destroy(global.jamm_fg_heads);
            ds_list_destroy(global.jamm_fg_orders);
            """);
        AddEvent(EventType.Step, (uint)EventSubtypeStep.BeginStep, FishGridScripts.Reset);
        AddEvent(EventType.Other, (uint)EventSubtypeOther.RoomStart, FishGridScripts.Reset);
        Queue(avoid, FishGridScripts.Avoidance(source));
        ModObjects.Compile(compiler);

        foreach (var room in data.Rooms)
            ModObjects.PlaceInstance(data, room.Name.Content, controller, first: true);

        var dirty = Function("jamm_fg_dirty");
        var touch = Function("jamm_fg_touch");
        var enter = Function("jamm_fg_enter");
        var leave = Function("jamm_fg_leave");
        var lifecycle = Lifecycle.Keys.ToDictionary(call => call, call => Function(WrapperName(call.Name, call.Count)));
        var relatedObjects = data.GameObjects.Select(obj => Related(obj, fishLarge)).ToArray();

        foreach (var code in originals)
            if (LegacyCodeHooks.Insert(code, Select, code == endStep ? enter : null, code == endStep ? leave : null))
                edits.Add(code);

        return Decision("enabled (FishLarge avoidance with ordered cells and live-position guards)");

        UndertaleFunction Function(string name) => data.Functions.EnsureDefined(name, data.Strings);

        void AddScript(string name, string body)
        {
            var code = UndertaleCode.CreateEmptyEntry(data, "gml_Script_" + name);
            data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(name), Code = code });
            Queue(code, body);
        }

        void AddEvent(EventType type, uint subtype, string body)
            => Queue(controller.EventHandlerFor(type, subtype, data), body);

        void Queue(UndertaleCode code, string body)
        {
            edits.Add(code);
            compiler.QueueCodeReplace(code, body);
        }

        LegacyCodeHooks.Hook Select(UndertaleInstruction instruction)
        {
            if (instruction.Kind == Opcode.Call && instruction.ValueFunction?.Name.Content is { } name)
            {
                if (lifecycle.TryGetValue((name, instruction.ArgumentsCount), out var wrapper))
                    return new(ReplaceCall: wrapper);
                if (SelfMovement.Contains(name)) return new(After: touch);
                if (name.StartsWith("instance_activate_", StringComparison.Ordinal)
                    || name.StartsWith("instance_deactivate_", StringComparison.Ordinal)
                    || name.StartsWith("room_", StringComparison.Ordinal)
                    || name.StartsWith("physics_", StringComparison.Ordinal)
                    || name.StartsWith("action_create_", StringComparison.Ordinal)
                    || name is "execute_string" or "execute_file" or "external_call"
                        or "variable_instance_set" or "game_restart" or "game_load"
                        or "action_kill_object" or "action_change_object" or "instance_create" or "instance_copy"
                        or "instance_destroy" or "instance_change" or "instance_create_layer" or "instance_create_depth")
                    return new(Before: dirty);
            }
            if (instruction.Kind != Opcode.Pop || instruction.ValueVariable?.Name.Content is not ("x" or "y" or "path_position" or "phy_position_x" or "phy_position_y"))
                return default;
            if (instruction.TypeInst is InstanceType.Local or InstanceType.Global or InstanceType.Arg)
                return default;
            if (instruction.ReferenceType != VariableType.Normal) return new(Before: dirty);
            if (instruction.TypeInst is InstanceType.Self or InstanceType.Builtin) return new(After: touch);
            int index = (int)instruction.TypeInst;
            return index >= 0 && index < relatedObjects.Length && !relatedObjects[index] ? default : new(Before: dirty);
        }
    }

    private static bool MayPerformEndStep(UndertaleCode code)
    {
        var instructions = code.Instructions;
        for (int i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].Kind != Opcode.Call) continue;
            if (instructions[i].ValueFunction?.Name.Content == "event_perform_object") return true;
            if (instructions[i].ValueFunction?.Name.Content != "event_perform") continue;

            // GM1 pushes arguments in reverse order. The first argument must be an
            // immediate non-Step event type, optionally converted to a VM variable.
            int index = i - 1;
            if (index >= 0 && instructions[index].Kind == Opcode.Conv && instructions[index].Type2 == DataType.Variable)
                index--;
            if (index < 0) return true;
            var selector = instructions[index];
            double? eventType = selector.Kind switch
            {
                Opcode.PushI => selector.ValueShort,
                Opcode.Push when selector.Type1 == DataType.Int16 => selector.ValueShort,
                Opcode.Push when selector.Type1 == DataType.Int32 => selector.ValueInt,
                Opcode.Push when selector.Type1 == DataType.Double => selector.ValueDouble,
                _ => null
            };
            if (eventType is null || eventType == (int)EventType.Step) return true;
        }
        return false;
    }

    private static bool Related(UndertaleGameObject first, UndertaleGameObject second)
        => Inherits(first, second) || Inherits(second, first);

    private static bool Inherits(UndertaleGameObject obj, UndertaleGameObject ancestor)
    {
        for (var current = obj; current is not null; current = current.ParentId)
            if (current == ancestor) return true;
        return false;
    }

    private static string Decision(string result) => $"[CGF-PERF-GRID] fish_avoid_fish: {result}";

    private static bool Matches(string source)
    {
        // Perf's earlier optimization hoists exactly two invariants. Normalize those
        // substitutions before checking the same loop fingerprint.
        string canonical = Whitespace().Replace(source, "");
        const string perfPrefix = "varjamm_perf_radius2=argument1*argument1;varjamm_perf_force=100000*argument2;";
        if (canonical.StartsWith(perfPrefix, StringComparison.Ordinal))
            canonical = canonical[perfPrefix.Length..]
                .Replace("jamm_perf_radius2", "(argument1*argument1)", StringComparison.Ordinal)
                .Replace("jamm_perf_force", "(100000*argument2)", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) == VanillaPattern;
    }

    private static string WrapperName(string name, ushort count) => $"jamm_fg_{name}_{count}";

    private static readonly IReadOnlyDictionary<(string Name, ushort Count), string> Lifecycle =
        new Dictionary<(string, ushort), string>
        {
            [("instance_create", 3)] = "jamm_fg_membership(argument2); return instance_create(argument0, argument1, argument2);",
            [("instance_copy", 1)] = "jamm_fg_membership(id); return instance_copy(argument0);",
            [("instance_destroy", 0)] = "jamm_fg_membership(id); return instance_destroy();",
            [("instance_destroy", 1)] = "jamm_fg_membership(argument0); return instance_destroy(argument0);",
            [("instance_destroy", 2)] = "jamm_fg_membership(argument0); return instance_destroy(argument0, argument1);",
            [("instance_change", 2)] = "jamm_fg_membership(id); jamm_fg_membership(argument0); return instance_change(argument0, argument1);",
            [("instance_deactivate_object", 1)] = "jamm_fg_membership(argument0); return instance_deactivate_object(argument0);",
            [("instance_activate_object", 1)] = "jamm_fg_membership(argument0); return instance_activate_object(argument0);"
        };

    private static readonly HashSet<string> SelfMovement = new(StringComparer.Ordinal)
    {
        "path_start", "path_end", "move_wrap", "move_snap", "move_random", "move_contact_solid",
        "move_contact_all", "move_outside_solid", "move_outside_all", "move_bounce_solid",
        "move_bounce_all", "mp_linear_step", "mp_linear_step_object", "mp_potential_step",
        "mp_potential_step_object", "action_move_to", "action_snap", "action_wrap", "action_move_random",
        "action_move_contact", "action_bounce"
    };
}
