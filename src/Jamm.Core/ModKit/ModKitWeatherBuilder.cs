using Jamm.Core.Building;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.Core.ModKit;

public static partial class ModKitBuilder
{
    private static void AddWeathers(UndertaleData data, CompileGroup compiler, ModKitManifest package,
        string modIdent, ref int scriptsAdded)
    {
        if (package.Weathers.Count == 0) return;
        foreach (string script in WeatherRuntime.PublicScripts)
            if (EnsurePlaceholder(data, compiler, script))
                scriptsAdded++;

        var fish = package.Fish.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var weather in package.Weathers)
        {
            string prefix = $"jmk_{modIdent}_weather_{SanitizeIdent(weather.Id)}";
            var hooks = new WeatherHooks(
                AddHookScript(data, compiler, prefix + "_start", weather.Hooks.Start, ref scriptsAdded),
                AddHookScript(data, compiler, prefix + "_step", weather.Hooks.Step, ref scriptsAdded),
                AddHookScript(data, compiler, prefix + "_end", weather.Hooks.End, ref scriptsAdded));
            var layers = weather.Background.Select(l => l with { Sprite = CompositionName(modIdent, l.Sprite) }).ToArray();
            var population = weather.Fish.Select(key => new WeatherFish($"jmk_{modIdent}_{SanitizeIdent(key)}", fish[key].Spawn)).ToArray();
            var compiled = weather with
            {
                Background = layers,
                Hooks = hooks,
                Music = ImportAudio(weather.Music, prefix + "_music"),
                Ambience = ImportAudio(weather.Ambience, prefix + "_ambience")
            };
            AddScript(data, compiler, $"{WeatherDeclaration.Prefix}{modIdent}_{SanitizeIdent(weather.Id)}",
                WeatherDeclaration.Encode(new(package.Id, compiled, population)));
            scriptsAdded++;
        }

        WeatherAudio? ImportAudio(WeatherAudio? audio, string name)
        {
            if (audio is null) return null;
            if (data.Sounds.ByName(name) is not null)
                throw new InvalidDataException($"Sound '{name}' already exists. Choose a different weather or package id.");
            var payload = new UndertaleEmbeddedAudio { Name = data.Strings.MakeString(name), Data = File.ReadAllBytes(audio.Sound) };
            data.EmbeddedAudio.Add(payload);
            int group = data.GetBuiltinSoundGroupID();
            data.Sounds.Add(new UndertaleSound
            {
                Name = data.Strings.MakeString(name),
                Flags = UndertaleSound.AudioEntryFlags.Regular | UndertaleSound.AudioEntryFlags.IsCompressed,
                Type = data.Strings.MakeString(".ogg"),
                File = data.Strings.MakeString(name + ".ogg"),
                GroupID = group,
                AudioGroup = group < data.AudioGroups.Count ? data.AudioGroups[group] : null,
                AudioFile = payload,
                Pitch = 1
            });
            return audio with { Sound = name };
        }
    }
}
