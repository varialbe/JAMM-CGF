using System.Globalization;
using System.Text;
using Jamm.Core.Building;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.ModKit;

/// <summary>Summary of a built standalone ModKit <c>.win</c> archive.</summary>
public sealed record ModKitBuildResult(
    string OutputPath,
    ModKitManifest Package,
    int SpritesAdded,
    int ObjectsAdded,
    int ScriptsAdded)
{
    /// <summary>The atlas decision for the standalone build, when repacking was enabled and textures were present.</summary>
    public string? TextureDecision { get; init; }
}

/// <summary>Compiles a validated <see cref="ModKitManifest"/> onto a read-only legacy base archive as a zero-conflict mod.</summary>
public static partial class ModKitBuilder
{
    /// <summary>Builds a ModKit folder package into a new verified <c>.win</c> file without modifying the base archive.</summary>
    public static ModKitBuildResult Build(
        string packageDirectory,
        string basePath,
        string outputPath,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default,
        bool noRepack = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = ModKitPackage.Load(packageDirectory);
        if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath))
            throw new FileNotFoundException($"Base archive '{basePath}' was not found. Configure a legacy 2023 base with 'cgfmm config set base <data.win>' or pass --base.", basePath);

        string destination = Path.GetFullPath(outputPath);
        BuildFiles.ValidateNewOutput(destination, [basePath]);

        progress?.Invoke($"Loading legacy base '{Path.GetFileName(basePath)}' read-only…");
        using (var inspectionFile = GameFileLoader.Load(basePath, progress, cancellationToken))
        {
            var info = inspectionFile.Inspection;
            if (info.IsYyc || !info.BytecodeSupported || info.BytecodeVersion != 16)
                throw new InvalidDataException($"'{Path.GetFileName(basePath)}' is not a supported legacy VM bytecode 16 archive. Select the August 2023 legacy data.win.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var input = new CancellableGameStream(basePath, cancellationToken);
        using var data = UndertaleIO.Read(input, (_, _) => cancellationToken.ThrowIfCancellationRequested(), _ => cancellationToken.ThrowIfCancellationRequested());

        var (spritesAdded, objectsAdded, scriptsAdded) = Apply(data, package, progress, cancellationToken);
        string? textureDecision = null;
        if (!noRepack && TextureRepacker.HasAtlas(data))
            textureDecision = TextureRepacker.RepackForBuild(data, outputPath, progress, cancellationToken);

        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".jmk-{Guid.NewGuid():N}.tmp");
        try
        {
            progress?.Invoke($"Writing ModKit archive '{Path.GetFileName(destination)}'…");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                UndertaleIO.Write(stream, data);

            ArchiveVerifier.Verify(temporary, progress: progress, cancellationToken: cancellationToken);
            BuildFiles.ValidateNewOutput(destination, [basePath]);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return new ModKitBuildResult(destination, package, spritesAdded, objectsAdded, scriptsAdded) { TextureDecision = textureDecision };
    }

    /// <summary>Applies a validated <see cref="ModKitManifest"/> to an in-memory <see cref="UndertaleData"/> model and compiles all generated GML.</summary>
    public static (int SpritesAdded, int ObjectsAdded, int ScriptsAdded) Apply(
        UndertaleData data,
        ModKitManifest package,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequiredParents(data, package);

        string modIdent = SanitizeIdent(package.Id);
        var spriteSpecs = CollectSpriteSpecs(package, modIdent);

        progress?.Invoke($"Packing {spriteSpecs.Count} sprite(s) for '{package.Name}'…");
        var sprites = ModKitSpriteImporter.ImportAll(data, spriteSpecs);

        var compiler = new CompileGroup(data);
        int objectsAdded = 0;
        int scriptsAdded = 0;

        // Placeholder stubs for called jamm_* API functions so the mod compiles standalone.
        if (package.Fish.Count > 0 && EnsurePlaceholder(data, compiler, "jamm_fish_register")) scriptsAdded++;
        if (package.Boats.Count > 0 && EnsurePlaceholder(data, compiler, "jamm_boat_register")) scriptsAdded++;
        if (package.Rods.Count > 0 && EnsurePlaceholder(data, compiler, "jamm_rod_register")) scriptsAdded++;
        if (package.Hats.Count > 0 && EnsurePlaceholder(data, compiler, "jamm_hat_register")) scriptsAdded++;
        if (package.Upgrades.Count > 0 && EnsurePlaceholder(data, compiler, "jamm_upgrade_register")) scriptsAdded++;

        var initBody = new StringBuilder();
        bool hasFishLunge = data.Scripts.ByName("fish_lunge") is not null;
        bool hasFleeLarge = data.Scripts.ByName("fish_fleelarge") is not null;
        bool hasFleeHuge = data.Scripts.ByName("fish_fleehuge") is not null;

        foreach (var entry in package.Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = $"jmk_{modIdent}_object_{SanitizeIdent(entry.Key)}";
            var sprite = entry.Sprite is { } key ? sprites[CompositionName(modIdent, key)] : null;
            var obj = CreateModObject(data, name, null, sprite, entry.Depth);
            QueueHook(EventType.Create, entry.Create);
            QueueHook(EventType.Step, entry.Step);
            QueueHook(EventType.Draw, entry.Draw);
            objectsAdded++;

            void QueueHook(EventType type, string? path)
            {
                if (path is not null)
                    compiler.QueueCodeReplace(obj.EventHandlerFor(type, 0u, data), File.ReadAllText(path, Encoding.UTF8));
            }
        }

        var weatherFish = package.Weathers.SelectMany(w => w.Fish).ToHashSet(StringComparer.Ordinal);
        foreach (var fish in package.Fish)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string keyIdent = SanitizeIdent(fish.Key);
            string assetPrefix = $"jmk_{modIdent}_{keyIdent}";
            string spriteName = fish.SpriteKey is null ? $"{assetPrefix}_spr" : CompositionName(modIdent, fish.SpriteKey);
            string objName = assetPrefix;

            string? createHook = AddHookScript(data, compiler, $"{assetPrefix}_create", fish.Hooks.Create, ref scriptsAdded);
            string? stepHook = AddHookScript(data, compiler, $"{assetPrefix}_step", fish.Hooks.Step, ref scriptsAdded);
            string? caughtHook = AddHookScript(data, compiler, $"{assetPrefix}_caught", fish.Hooks.Caught, ref scriptsAdded);
            string? drawHook = AddHookScript(data, compiler, $"{assetPrefix}_draw", fish.Hooks.Draw, ref scriptsAdded);

            var obj = CreateModObject(data, objName, data.GameObjects.ByName(fish.ParentObject)!, sprites[spriteName], depth: 0);
            objectsAdded++;

            var createGml = new StringBuilder();
            createGml.AppendLine("hasPath = 0;");
            createGml.AppendLine("PATH_MIN_SPEED = 0;");
            createGml.AppendLine("PATH_THRUST_SPEED = 0;");
            createGml.AppendLine("PATH_THRUST_PHASE = 0;");
            createGml.AppendLine("PATH_THRUSTS = 0;");
            createGml.AppendLine($"jamm_swim_speed = {Num(fish.Speed)};");
            createGml.AppendLine($"hspeed = random({Num(fish.Speed * 2)}) - {Num(fish.Speed)};");
            createGml.AppendLine($"vspeed = random({Num(fish.Speed * 2)}) - {Num(fish.Speed)};");
            createGml.AppendLine($"LUNGE_RANGE_START = {Num(fish.LungeRange)};");
            createGml.AppendLine($"LUNGE_RANGE_STOP = {Num(fish.LungeRangeStop)};");
            createGml.AppendLine($"LUNGE_SPEED = {Num(fish.LungeSpeed)};");
            createGml.AppendLine($"BASE_VALUE = {Num(fish.Value)};");
            createGml.AppendLine($"XP_VALUE = {Num(fish.Xp)};");
            createGml.AppendLine("event_inherited();");
            createGml.AppendLine($"stayOutAtNight = {(fish.Night ? 1 : 0)};");
            createGml.AppendLine($"MAX_STAMINA = {Num(fish.Stamina)};");
            createGml.AppendLine("stamina = MAX_STAMINA;");
            if (createHook is not null)
                createGml.Append(createHook).AppendLine("();");
            compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Create, 0u, data), createGml.ToString());

            // Override Fish_Other_10 so path-free fish do not invoke path_thrust_update().
            var idleGml = new StringBuilder();
            if (fish.ParentObject == "FishMedium" && hasFleeLarge)
                idleGml.AppendLine("fish_fleelarge();");
            else if (fish.ParentObject == "FishLarge" && hasFleeHuge)
                idleGml.AppendLine("fish_fleehuge();");
            idleGml.AppendLine("visible = true;");
            idleGml.AppendLine("full_reset = 0;");
            idleGml.AppendLine("life = MAX_LIFE;");
            if (hasFishLunge)
                idleGml.AppendLine("fish_lunge();");
            idleGml.AppendLine(
                """
                var jamm_dist, jamm_dir, jamm_spd;
                jamm_dist = point_distance(x, y, start_x, start_y);
                if (jamm_dist > 180)
                {
                    jamm_dir = point_direction(x, y, start_x, start_y);
                    hspeed += lengthdir_x(0.12, jamm_dir);
                    vspeed += lengthdir_y(0.12, jamm_dir);
                }
                else if (random(1) < 0.03)
                {
                    jamm_dir = random(360);
                    hspeed += lengthdir_x(0.5, jamm_dir);
                    vspeed += lengthdir_y(0.5, jamm_dir);
                }
                if (y < 1294) vspeed += 1;
                if (x < 600) hspeed += 0.5;
                jamm_spd = max(0.5, jamm_swim_speed);
                if (speed > (jamm_spd * 1.6)) speed = jamm_spd * 1.6;
                if (speed < (jamm_spd * 0.5)) speed = jamm_spd * 0.5;
                """);
            compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User0, data), idleGml.ToString());

            if (stepHook is not null)
                compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Step, (uint)EventSubtypeStep.EndStep, data), $"event_inherited();\n{stepHook}();\n");
            if (caughtHook is not null)
                compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User9, data), $"event_inherited();\n{caughtHook}();\n");
            if (drawHook is not null)
                compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Draw, (uint)EventSubtypeDraw.Draw, data), $"event_inherited();\n{drawHook}();\n");

            initBody.AppendLine(
                $"jamm_fish_register({GmlString($"{package.Id}:{fish.Key}")}, {objName}, {fish.SizeTab}, {GmlString(fish.Name)}, " +
                $"{GmlString(fish.Description)}, {Num(fish.Spawn.XMin)}, {Num(fish.Spawn.XMax)}, {Num(fish.Spawn.YMin)}, {Num(fish.Spawn.YMax)}, {(fish.Spawn.Area is null && !weatherFish.Contains(fish.Key) ? fish.Spawn.Count : 0)}, " +
                $"{GmlString(fish.CaughtDescription)}, {GmlString(fish.SeenDescription)}, {GmlString(fish.HiddenDescription)}, " +
                $"{GmlString(fish.Description)}, {GmlString(fish.Description)}, {(fish.Exotic ? 1 : 0)});");
        }

        foreach (var boat in package.Boats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string keyIdent = SanitizeIdent(boat.Key);
            string assetPrefix = $"jmk_{modIdent}_{keyIdent}";
            string spriteName = boat.SpriteKey is { } spriteKey ? CompositionName(modIdent, spriteKey) : $"{assetPrefix}_spr";
            string cardName = boat.CardKey is { } cardKey ? CompositionName(modIdent, cardKey) : $"{assetPrefix}_card";
            string objName = assetPrefix;

            var obj = CreateModObject(data, objName, data.GameObjects.ByName("Boat")!, sprites[spriteName], depth: -20);
            objectsAdded++;

            string createGml = $"""
                event_inherited();
                fish_limit = {boat.Stats.Capacity};
                base_limit = fish_limit;
                move_speed = {Num(boat.Stats.MaxSpeed)};
                base_speed = move_speed;
                move_bonus = {Num(boat.Stats.MoveBonus)};
                MAX_X = {Num(boat.Stats.MaxDistance)};
                min_angle = -2;
                max_angle = 2;
                deposit_start = 335;
                deposit_stop = -30;
                deposit_depth = 60;
                boat_index = 0;
                """;
            var hooks = boat.Hooks;
            if (hooks?.Create is { } create)
                createGml += "\n" + File.ReadAllText(create, Encoding.UTF8);
            compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Create, 0u, data), createGml);
            QueueVehicleEvent(hooks?.BeginStep, EventType.Step, (uint)EventSubtypeStep.BeginStep);
            QueueVehicleEvent(hooks?.Step, EventType.Step, (uint)EventSubtypeStep.Step);
            QueueVehicleEvent(hooks?.EndStep, EventType.Step, (uint)EventSubtypeStep.EndStep);
            QueueVehicleEvent(hooks?.DrawGui, EventType.Draw, (uint)EventSubtypeDraw.DrawGUI);
            QueueVehicleEvent(hooks?.Destroy, EventType.Destroy, 0, "\nevent_inherited();");

            initBody.AppendLine(
                $"jamm_boat_register({GmlString($"{package.Id}:{boat.Key}")}, {GmlString(boat.Name)}, {Num(boat.Cost)}, " +
                $"{cardName}, {objName}, {GmlString(boat.Description)}, {(boat.Unlocked ? 1 : 0)});");

            void QueueVehicleEvent(string? path, EventType type, uint subtype, string suffix = "")
            {
                if (path is not null)
                    compiler.QueueCodeReplace(obj.EventHandlerFor(type, subtype, data), File.ReadAllText(path, Encoding.UTF8) + suffix);
            }
        }

        foreach (var rod in package.Rods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            initBody.AppendLine(RodRegistration(package.Id, rod));
        }

        foreach (var hat in package.Hats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string keyIdent = SanitizeIdent(hat.Key);
            string assetPrefix = $"jmk_{modIdent}_{keyIdent}";
            string spriteName = $"{assetPrefix}_spr";
            string objName = assetPrefix;

            string? effectHook = AddHookScript(data, compiler, $"{assetPrefix}_effect", hat.Hooks.Effect, ref scriptsAdded);
            var obj = CreateModObject(data, objName, data.GameObjects.ByName("Gear")!, sprites[spriteName], depth: -4);
            objectsAdded++;

            string createGml = $"""
                event_inherited();
                name = {GmlString(hat.Name)};
                desc = {GmlString(hat.Description)};
                EQUIP_OFFSET_X = {Num(hat.OffsetX)};
                EQUIP_OFFSET_Y = {Num(hat.OffsetY)};
                """;
            compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Create, 0u, data), createGml);

            if (effectHook is not null)
                compiler.QueueCodeReplace(obj.EventHandlerFor(EventType.Other, (uint)EventSubtypeOther.User5, data), $"event_inherited();\n{effectHook}();\n");

            initBody.AppendLine(
                $"jamm_hat_register({GmlString($"{package.Id}:{hat.Key}")}, {objName}, {Num(hat.Value)}, {Num(hat.Weight)}, " +
                $"{Num(hat.RangeMin)}, {Num(hat.RangeMax)}, {Num(hat.HatalogOffsetX)}, {Num(hat.HatalogOffsetY)}, {(hat.Mythic ? 1 : 0)});");
        }

        foreach (var upgrade in package.Upgrades)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string keyIdent = SanitizeIdent(upgrade.Key);
            string assetPrefix = $"jmk_{modIdent}_{keyIdent}";
            string iconName = $"{assetPrefix}_icon";

            string? applyHook = AddHookScript(data, compiler, $"{assetPrefix}_apply", upgrade.Hooks.Apply, ref scriptsAdded);
            string? stepHook = AddHookScript(data, compiler, $"{assetPrefix}_step", upgrade.Hooks.Step, ref scriptsAdded);

            initBody.AppendLine(
                $"jamm_upgrade_register({GmlString($"{package.Id}:{upgrade.Key}")}, {GmlString(upgrade.Name)}, " +
                $"{GmlString(upgrade.Description)}, {GmlString(upgrade.Description2)}, {Num(upgrade.Cost)}, {iconName}, " +
                $"{upgrade.MaxStacks}, {applyHook ?? "-1"}, {stepHook ?? "-1"});");
        }

        AddWeathers(data, compiler, package, modIdent, ref scriptsAdded);

        AreaPlacement.ValidateSource(data, package);
        foreach (var area in package.Areas)
        {
            var population = package.Fish.Where(f => f.Spawn.Area == area.Id).Select(f =>
                new AreaFish($"jmk_{modIdent}_{SanitizeIdent(f.Key)}", f.Spawn.Count, f.Spawn.Within)).ToArray();
            var opening = area.Opens;
            if (opening?.Sprite is { } sprite && package.Sprites.Any(s => s.Key == sprite))
                opening = opening with { Sprite = CompositionName(modIdent, sprite) };
            AddScript(data, compiler, $"{AreaDeclarations.Prefix}{modIdent}_{SanitizeIdent(area.Id)}",
                AreaDeclarations.Encode(new AreaDeclaration(package.Id, area with { Opens = opening }, population)));
            scriptsAdded++;
        }

        if (initBody.Length > 0)
        {
            initBody.AppendLine("return 0;");
            AddScript(data, compiler, $"jamm_init_{modIdent}", initBody.ToString());
            scriptsAdded++;
        }

        progress?.Invoke($"Compiling {scriptsAdded} script(s) and {objectsAdded} object(s)…");
        ArchiveVerifier.Compile(compiler, $"ModKit package '{package.Id}'");

        return (sprites.Count, objectsAdded, scriptsAdded);
    }

    private static void ValidateRequiredParents(UndertaleData data, ModKitManifest package)
    {
        foreach (string parent in package.Fish.Select(f => f.ParentObject).Distinct(StringComparer.Ordinal))
            RequireObject(data, parent);
        if (package.Boats.Count > 0)
            RequireObject(data, "Boat");
        if (package.Hats.Count > 0)
            RequireObject(data, "Gear");

        static void RequireObject(UndertaleData data, string name)
        {
            if (data.GameObjects.ByName(name) is null)
                throw new InvalidDataException($"Required base object '{name}' was not found. Build ModKit mods from the legacy 2023 base data.win.");
        }
    }

    private static List<SpriteImportSpec> CollectSpriteSpecs(ModKitManifest package, string modIdent)
    {
        var specs = new List<SpriteImportSpec>();
        var tiledWeatherSprites = package.Weathers.SelectMany(w => w.Background)
            .Where(layer => layer.Repeat).Select(layer => layer.Sprite).ToHashSet(StringComparer.Ordinal);
        foreach (var recipe in package.Sprites)
            specs.Add(new(CompositionName(modIdent, recipe.Key), "", recipe.Frames.Count, recipe.Origin,
                CenterOrigin: true, GenerateMask: true, recipe, WrapHorizontal: tiledWeatherSprites.Contains(recipe.Key)));

        foreach (var fish in package.Fish.Where(f => f.SpriteKey is null))
        {
            string keyIdent = SanitizeIdent(fish.Key);
            specs.Add(new($"jmk_{modIdent}_{keyIdent}_spr", fish.SpritePath, fish.Frames, fish.Origin, CenterOrigin: true, GenerateMask: true));
        }
        foreach (var boat in package.Boats)
        {
            string keyIdent = SanitizeIdent(boat.Key);
            if (boat.SpriteKey is null)
                specs.Add(new($"jmk_{modIdent}_{keyIdent}_spr", boat.SpritePath, 1, boat.Origin, CenterOrigin: true, GenerateMask: false));
            if (boat.CardKey is null)
                specs.Add(new($"jmk_{modIdent}_{keyIdent}_card", boat.CardPath, 1, null, CenterOrigin: true, GenerateMask: false));
        }
        foreach (var rod in package.Rods)
        {
            specs.Add(new(RodSpriteName(modIdent, rod), rod.SpritePath, 1, rod.Origin, CenterOrigin: true, GenerateMask: false));
        }
        foreach (var hat in package.Hats)
        {
            string keyIdent = SanitizeIdent(hat.Key);
            specs.Add(new($"jmk_{modIdent}_{keyIdent}_spr", hat.SpritePath, 1, hat.Origin, CenterOrigin: true, GenerateMask: true));
        }
        foreach (var upgrade in package.Upgrades)
        {
            string keyIdent = SanitizeIdent(upgrade.Key);
            specs.Add(new($"jmk_{modIdent}_{keyIdent}_icon", upgrade.IconPath, 1, null, CenterOrigin: false, GenerateMask: false));
        }
        return specs;
    }

    private static string CompositionName(string id, string key) => $"jmk_{id}_recipe_{SanitizeIdent(key)}";

    private static bool EnsurePlaceholder(UndertaleData data, CompileGroup compiler, string scriptName)
    {
        if (data.Scripts.ByName(scriptName) is not null)
            return false;
        AddScript(data, compiler, scriptName, "return -1;\n");
        return true;
    }

    private static string? AddHookScript(UndertaleData data, CompileGroup compiler, string scriptName, string? filePath, ref int scriptsAdded)
    {
        if (filePath is null) return null;
        string body = File.ReadAllText(filePath, Encoding.UTF8);
        AddScript(data, compiler, scriptName, body);
        scriptsAdded++;
        return scriptName;
    }

    private static void AddScript(UndertaleData data, CompileGroup compiler, string scriptName, string body)
    {
        if (data.Scripts.ByName(scriptName) is not null)
            throw new InvalidDataException($"Script '{scriptName}' already exists in the base archive. Choose a different mod id or key.");

        string codeName = "gml_Script_" + scriptName;
        if (data.Code.ByName(codeName) is not null)
            throw new InvalidDataException($"Code entry '{codeName}' already exists in the base archive. Choose a different mod id or key.");

        var code = UndertaleCode.CreateEmptyEntry(data, codeName);
        data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(scriptName), Code = code });
        compiler.QueueCodeReplace(code, body);
    }

    private static UndertaleGameObject CreateModObject(
        UndertaleData data,
        string name,
        UndertaleGameObject? parent,
        UndertaleSprite? sprite,
        int depth)
    {
        if (data.GameObjects.ByName(name) is not null)
            throw new InvalidDataException($"Object '{name}' already exists in the base archive. Choose a different mod id or key.");

        var obj = new UndertaleGameObject
        {
            Name = data.Strings.MakeString(name),
            ParentId = parent,
            Sprite = sprite,
            Visible = true,
            Persistent = false,
            Depth = depth
        };
        data.GameObjects.Add(obj);
        return obj;
    }

    internal static string SanitizeIdent(string value) => value.Replace('-', '_');

    internal static string RodRegistration(string id, ModKitRod rod) =>
        $"jamm_rod_register({GmlString($"{id}:{rod.Key}")}, {GmlString(rod.Name)}, {RodNumber(rod.Cost)}, {RodNumber(rod.CastSpeed)}, " +
        $"{RodNumber(rod.RodPower)}, {RodNumber(rod.RodPowerFlat)}, {RodNumber(rod.ReelSpeed)}, {RodNumber(rod.SinkerDistance)}, " +
        $"{RodSpriteName(SanitizeIdent(id), rod)}, {rod.UpgradeSlots}, {(rod.Unlocked ? 1 : 0)});";

    private static string RodSpriteName(string modIdent, ModKitRod rod) => $"jmk_{modIdent}_{SanitizeIdent(rod.Key)}_rod_sprite";

    private static string Num(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string RodNumber(double value)
    {
        // The legacy compiler accepts plain decimal literals. Keep the parsed number's precision.
        string literal = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int exponent = literal.IndexOf('E');
        if (exponent >= 0)
        {
            string mantissa = literal[..exponent];
            int point = mantissa.IndexOf('.');
            int offset = (point < 0 ? mantissa.Length : point)
                + int.Parse(literal.AsSpan(exponent + 1), CultureInfo.InvariantCulture);
            string digits = mantissa.Replace(".", "", StringComparison.Ordinal);
            literal = offset <= 0 ? "0." + new string('0', -offset) + digits
                : offset >= digits.Length ? digits.PadRight(offset, '0') : digits.Insert(offset, ".");
        }
        return value < 0 ? "-" + literal : literal;
    }

    internal static string GmlString(string value)
    {
        string normalized = value.Replace("\r\n", "#", StringComparison.Ordinal).Replace("\n", "#", StringComparison.Ordinal);
        if (!normalized.Contains('"', StringComparison.Ordinal))
            return $"\"{normalized}\"";
        string[] parts = normalized.Split('"');
        return string.Join(" + chr(34) + ", parts.Select(p => $"\"{p}\""));
    }
}
