using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

/// <summary>Adds an independent hatch and flooded salvage area to legacy Cat Goes Fishing.</summary>
public static class SiloMod
{
    /// <summary>Stable mod identifier used by the recipe library.</summary>
    public const string ModId = "jamm-silo";
    /// <summary>Independent maintenance hatch placed in NewWorld.</summary>
    public const string HatchObjectName = "obj_jamm_silo_hatch";
    /// <summary>Controller that carries the visit and checkpoint state across rooms.</summary>
    public const string ControllerObjectName = "obj_jamm_silo_ctrl";
    /// <summary>Hatch lamp integrated with the game's lighting renderer.</summary>
    public const string BeaconObjectName = "obj_jamm_silo_beacon";
    /// <summary>Diver and local gameplay controller in the silo.</summary>
    public const string AreaObjectName = "obj_jamm_silo_area";
    /// <summary>Dedicated underwater silo room.</summary>
    public const string SanctuaryRoomName = "room_jamm_silo";

    // This entrance stays in the same clearing when a pack moves the original MissileSilo.
    private const int HatchX = 34136;
    private const int HatchY = 11496;

    /// <summary>Applies the Silo recipe to a compatible legacy archive in memory.</summary>
    public static void Apply(UndertaleData data, string? packageDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Rooms.Any(r => r.Name.Content.StartsWith("room_jamm_silo", StringComparison.Ordinal)) ||
            data.GameObjects.Any(o => o.Name.Content.StartsWith("obj_jamm_silo_", StringComparison.Ordinal)) ||
            data.Sprites.Any(s => s.Name.Content.StartsWith("spr_jamm_silo_", StringComparison.Ordinal)) ||
            data.Code.Any(c => c.Name.Content.StartsWith("gml_Object_obj_jamm_silo_", StringComparison.Ordinal)))
            throw new InvalidDataException("This archive already contains Silo assets. Choose a base without the Silo recipe.");

        var world = data.Rooms.ByName("NewWorld")
            ?? throw new InvalidDataException("The NewWorld room is missing. Choose the legacy August 2023 game archive.");
        foreach (string name in new[] { "MissileSilo", "CatSwim", "Girl", "Boat", "Rod", "Lure", "Camera", "BreathBar", "SaveManager" })
        {
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"The {name} object is missing. Choose a compatible legacy August 2023 archive.");
        }
        if (world.GameObjects.Any(i => Math.Abs((long)i.X - HatchX) <= 40 && Math.Abs((long)i.Y - HatchY) <= 30))
            throw new InvalidDataException("Another instance occupies the Silo hatch site in NewWorld. Choose a base with clear space near the original silo.");

        var sprites = SiloSprites.Import(data, packageDirectory);
        var hatch = ModObjects.Create(data, HatchObjectName, persistent: false);
        hatch.Depth = -19;
        var controller = ModObjects.Create(data, ControllerObjectName, persistent: true);
        var beacon = ModObjects.Create(data, BeaconObjectName, persistent: false);
        beacon.ParentId = data.GameObjects.ByName("AbstractLight");
        beacon.Sprite = sprites["spr_jamm_silo_glow"];
        beacon.Depth = 0;
        var area = ModObjects.Create(data, AreaObjectName, persistent: false);
        area.Sprite = sprites["spr_jamm_silo_diver"];
        area.Depth = 0;

        var room = CreateArea(data, area);
        var compiler = new CompileGroup(data);
        string font = data.Fonts.ByName("fnt_babySmall") is null ? "-1" : "fnt_babySmall";
        bool hasNav = data.GameObjects.ByName("NavBrain") is not null;
        bool hasHandler = data.GameObjects.ByName("Handler") is not null;

        Queue(hatch, EventType.Create, 0, SiloTravel.HatchCreate);
        Queue(hatch, EventType.Step, (uint)EventSubtypeStep.Step, SiloTravel.HatchStep(hasHandler));
        Queue(hatch, EventType.Draw, (uint)EventSubtypeDraw.Draw, SiloTravel.HatchDraw);
        Queue(hatch, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, SiloTravel.HatchGui(font));
        Queue(beacon, EventType.Create, 0, SiloTravel.BeaconCreate(beacon.ParentId is not null));

        Queue(controller, EventType.Create, 0, SiloTravel.ControllerCreate);
        Queue(controller, EventType.Other, (uint)EventSubtypeOther.User0, SiloTravel.Depart(hasNav));
        Queue(controller, EventType.Other, (uint)EventSubtypeOther.RoomStart, SiloTravel.RoomStart);
        Queue(controller, EventType.Step, (uint)EventSubtypeStep.BeginStep, SiloTravel.BeginStep(hasNav));

        Queue(area, EventType.Create, 0, SiloInterior.Create(data.Sounds.ByName("sfx_underwater_loop") is not null));
        Queue(area, EventType.Step, (uint)EventSubtypeStep.Step, SiloInterior.Step);
        Queue(area, EventType.Draw, (uint)EventSubtypeDraw.Draw, SiloInterior.Draw(font));
        Queue(area, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI, SiloInterior.DrawGui(font));

        ModObjects.Compile(compiler);
        Place(world, hatch, HatchX, HatchY);
        Place(room, area, SiloInterior.EntryX, SiloInterior.EntryY);

        void Queue(UndertaleGameObject obj, EventType type, uint subtype, string source) =>
            compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), source);

        void Place(UndertaleRoom target, UndertaleGameObject obj, int x, int y) =>
            target.GameObjects.Add(new UndertaleRoom.GameObject
            {
                InstanceID = ++data.GeneralInfo.LastObj,
                ObjectDefinition = obj,
                X = x,
                Y = y
            });
    }

    private static UndertaleRoom CreateArea(UndertaleData data, UndertaleGameObject area)
    {
        var room = new UndertaleRoom
        {
            Name = data.Strings.MakeString(SanctuaryRoomName),
            Caption = data.Strings.MakeString("The Flooded Silo"),
            Width = 1520,
            Height = 880,
            Speed = 30,
            DrawBackgroundColor = true,
            BackgroundColor = 0xFF201411,
            Flags = UndertaleRoom.RoomEntryFlags.EnableViews | UndertaleRoom.RoomEntryFlags.ClearViewBackground
        };
        var view = room.Views[0];
        view.Enabled = true;
        view.ViewWidth = 800;
        view.ViewHeight = 480;
        view.ViewX = SiloInterior.EntryX - 400;
        view.PortWidth = 1200;
        view.PortHeight = 720;
        view.BorderX = 400;
        view.BorderY = 240;
        view.SpeedX = -1;
        view.SpeedY = -1;
        view.ObjectId = area;
        data.Rooms.Add(room);
        data.GeneralInfo.RoomOrder.Add(new(room, data.Rooms.IndexOf(room)));
        return room;
    }
}
