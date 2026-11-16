using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Small helpers to add objects, room instances and compiled code to a data.win.</summary>
public static class ModObjects
{
    public static UndertaleGameObject Create(UndertaleData data, string name, bool persistent)
    {
        if (data.GameObjects.ByName(name) is not null)
            throw new InvalidDataException($"Object '{name}' already exists: build the mod from the ORIGINAL data.win, not from a modded one.");
        var obj = new UndertaleGameObject
        {
            Name = data.Strings.MakeString(name),
            Visible = true,
            Persistent = persistent,
            Depth = -1000000,
        };
        data.GameObjects.Add(obj);
        return obj;
    }

    public static void PlaceInstance(UndertaleData data, string roomName, UndertaleGameObject obj, bool first)
    {
        var room = data.Rooms.ByName(roomName) ?? throw new InvalidDataException($"Room '{roomName}' not found: unsupported game version.");
        var instance = new UndertaleRoom.GameObject
        {
            InstanceID = ++data.GeneralInfo.LastObj,
            ObjectDefinition = obj,
            X = 0,
            Y = 0,
        };
        if (first) room.GameObjects.Insert(0, instance); else room.GameObjects.Add(instance);
    }

    public static string Decompile(UndertaleData data, UndertaleCode code)
        => new Underanalyzer.Decompiler.DecompileContext(new GlobalDecompileContext(data), code, data.ToolInfo.DecompilerSettings).DecompileToString();

    public static void Compile(CompileGroup compiler)
    {
        var result = compiler.Compile();
        if (!result.Successful)
            throw new InvalidDataException("GML compile failed:\n" + result.PrintAllErrors(true));
    }
}
