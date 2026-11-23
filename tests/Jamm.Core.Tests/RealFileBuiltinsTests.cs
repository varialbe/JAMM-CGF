using System.Text.RegularExpressions;
using Jamm.Core.Building;
using Jamm.Core.ModKit;
using Jamm.Core.Patching;
using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Tests;

/// <summary>Opt-in regressions for the real legacy base, generated builtins, and published ModKit examples.</summary>
public sealed class RealFileBuiltinsTests
{
    [RealFileBuiltinsFact]
    public void BuiltinsAndModKitExamplesComposeWithValidAssetReferencesAndInstanceContracts()
    {
        string basePath = Environment.GetEnvironmentVariable("JAMM_REAL_BUILTINS_BASE")!;
        string root = FindRepositoryRoot();
        string tempPath = Path.Combine(Path.GetTempPath(), "jamm-real-builtins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        string originalHash = BuildFiles.Hash(basePath);
        try
        {
            string storePath = Path.Combine(tempPath, "store");
            Directory.CreateDirectory(storePath);
            string moduleVersion = typeof(Jamm.Core.Management.BuiltinMods).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];
            Assert.True(Jamm.Core.Management.BuiltinMods.GeneratorVersion == "9." + moduleVersion,
                "The builtin cache version must include the current generator module version.");
            var patches = new List<BuildInput>();
            var exampleArchives = new List<string>();
            using var original = GameFileLoader.Load(basePath);
            foreach (var builtin in Jamm.Core.Management.BuiltinMods.Catalog)
            {
                string archive = Path.Combine(storePath, builtin.Id + ".win");
                GenerateBuiltin(builtin.Id, basePath, archive);
                Assert.True(File.Exists(archive), $"Builtin '{builtin.Id}' was not written to the temporary store.");
                using var loaded = GameFileLoader.Load(archive);
                Assert.Contains(loaded.Data.Code, c => c.ParentEntry is null);
                patches.Add(new(PatchBuilder.Create(original, loaded, builtin.Name), archive));
                exampleArchives.Add(archive);
            }

            string examplesRoot = Path.Combine(root, "examples", "modkit");
            string[] packages = Directory.EnumerateDirectories(examplesRoot)
                .Where(path => File.Exists(Path.Combine(path, "jamm-mod.json")))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Contains(packages, p => Path.GetFileName(p) == "harbor-town");
            Assert.Contains(packages, p => Path.GetFileName(p) == "submarine");
            foreach (string package in packages)
            {
                string id = Path.GetFileName(package);
                string archive = Path.Combine(tempPath, id + ".win");
                ModKitBuilder.Build(package, basePath, archive, noRepack: true);
                using var mod = GameFileLoader.Load(archive);
                ModPatch modPatch = PatchBuilder.Create(original, mod, id);
                patches.Add(new(modPatch, archive));
                exampleArchives.Add(archive);

                IReadOnlyList<AssetLiteralFinding> findings = AssetLiteralAudit.Analyze(original, mod);
                if (id == "harbor-town")
                    Assert.Equal(3, findings.Count(f => f.Code == "gml_Object_jmk_harbor_town_object_harbor_Create_0"
                        && f.Container == "harbor_sprite" && f.CandidateFamilies.Contains("sprites", StringComparer.Ordinal)));
            }

            string composedPath = Path.Combine(tempPath, "profile-composed.win");
            var build = ArchiveBuilder.Build(basePath, patches, composedPath, noRepack: true);
            Assert.NotNull(build.Verification);
            using var composed = GameFileLoader.Load(composedPath);
            AssertHarborSpriteReferencesResolveByName(composed);
            AssertCrossModInstanceVariableContracts(exampleArchives, composed.Data);
            AssertNoConflictingGlobalVariableIds(composed.Data);
            Assert.Equal(originalHash, BuildFiles.Hash(basePath));
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
            Assert.Equal(originalHash, BuildFiles.Hash(basePath));
        }
    }

    private static void AssertHarborSpriteReferencesResolveByName(LoadedGameFile composed)
    {
        const string codeName = "gml_Object_jmk_harbor_town_object_harbor_Create_0";
        string code = PatchBuilder.ReadCode(composed, codeName).Text;
        string[] names = ["jmk_harbor_town_recipe_lighthouse", "jmk_harbor_town_recipe_aquarium", "jmk_harbor_town_recipe_statue"];
        var spriteIndices = composed.Data.Sprites.Select((sprite, index) => (sprite.Name.Content, index))
            .ToDictionary(pair => pair.Content, pair => pair.index, StringComparer.Ordinal);
        for (int slot = 0; slot < names.Length; slot++)
        {
            Assert.True(spriteIndices.TryGetValue(names[slot], out int expected), $"Composed profile is missing sprite '{names[slot]}'.");
            Match match = Regex.Match(code, $@"\bharbor_sprite\s*\[\s*{slot}\s*\]\s*=\s*(?<id>\d+)\s*;");
            Assert.True(match.Success, $"Composed Harbor Town code has no literal assignment for harbor sprite slot {slot}.");
            Assert.Equal(expected, int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void GenerateBuiltin(string id, string basePath, string outputPath)
    {
        using var input = GameFileLoader.Load(basePath);
        switch (id)
        {
            case "perf": PerfMod.Apply(input.Data, applyFishGrid: false, applyScheduler: false, applyCoreOptimizations: false, applyDecorSleep: false, applyFishSleep: false); break;
            case "smooth": SmoothMod.Apply(input.Data); break;
            case "hd-lite": HdMod.Apply(input.Data, null); break;
            case "console": ConsoleMod.Apply(input.Data); break;
            default: throw new InvalidOperationException($"Unknown builtin '{id}'.");
        }
        using var output = File.Create(outputPath);
        UndertaleIO.Write(output, input.Data);
    }

    private static void AssertCrossModInstanceVariableContracts(IEnumerable<string> exampleArchives, UndertaleData composed)
    {
        var reads = new HashSet<(string Object, string Variable)>();
        var assignments = new HashSet<(string Object, string Variable)>();
        var member = new Regex(@"\b(?<object>obj_jamm_[A-Za-z0-9_]+)\.(?<variable>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);
        var aliasDeclaration = new Regex(@"\b(?:var\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*asset_get_index\s*\(\s*""(?<object>obj_jamm_[A-Za-z0-9_]+)""\s*\)", RegexOptions.CultureInvariant);
        var aliasMember = new Regex(@"\b(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.(?<variable>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant);
        var instanceAssignment = new Regex(@"\b(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)", RegexOptions.CultureInvariant);
        foreach (string archive in exampleArchives)
        {
            using var file = GameFileLoader.Load(archive);
            foreach (var code in file.Data.Code.Where(c => c.ParentEntry is null))
            {
                var source = new CodeDecompiler(file.Data, Path.GetFileName(archive), _ => { }).Read(code);
                Assert.Equal("gml", source.Format);
                var aliases = aliasDeclaration.Matches(source.Text).Cast<Match>()
                    .ToDictionary(match => match.Groups["alias"].Value, match => match.Groups["object"].Value, StringComparer.Ordinal);
                foreach (Match match in member.Matches(source.Text))
                {
                    var key = (match.Groups["object"].Value, match.Groups["variable"].Value);
                    if (IsAssignment(source.Text, match.Index + match.Length))
                        assignments.Add(key);
                    else reads.Add(key);
                }
                foreach (Match match in aliasMember.Matches(source.Text))
                {
                    if (!aliases.TryGetValue(match.Groups["alias"].Value, out string? objectName)) continue;
                    var key = (objectName, match.Groups["variable"].Value);
                    if (IsAssignment(source.Text, match.Index + match.Length)) assignments.Add(key);
                    else reads.Add(key);
                }

                Match owner = Regex.Match(code.Name.Content, @"^gml_Object_(?<object>obj_jamm_[A-Za-z0-9_]+)_", RegexOptions.CultureInvariant);
                if (!owner.Success) continue;
                foreach (Match match in instanceAssignment.Matches(source.Text))
                    assignments.Add((owner.Groups["object"].Value, match.Groups["variable"].Value));
            }
        }

        var composedDecompiler = new CodeDecompiler(composed, "composed profile", _ => { });
        foreach (var code in composed.Code.Where(c => c.ParentEntry is null))
        {
            Match owner = Regex.Match(code.Name.Content, @"^gml_Object_(?<object>obj_jamm_[A-Za-z0-9_]+)_", RegexOptions.CultureInvariant);
            if (!owner.Success) continue;
            string source = composedDecompiler.Read(code).Text;
            foreach (Match match in instanceAssignment.Matches(source))
                assignments.Add((owner.Groups["object"].Value, match.Groups["variable"].Value));
        }
        var consoleStep = composed.Code.Single(c => c.Name.Content == "gml_Object_obj_jamm_console_Step_1");
        string consoleStepGml = composedDecompiler.Read(consoleStep).Text;
        Assert.Contains("jamm_console_open = global.jamm_console_open", consoleStepGml, StringComparison.Ordinal);
        assignments.Add((Jamm.ModMaker.ConsoleMod.ObjectName, "jamm_console_open"));
        var missing = reads.Except(assignments).OrderBy(x => x.Object, StringComparer.Ordinal)
            .ThenBy(x => x.Variable, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, "ModKit examples read object instance variables without an assignment: "
            + string.Join(", ", missing.Select(x => x.Object + "." + x.Variable)));
        Assert.NotEmpty(composed.Code);

        static bool IsAssignment(string text, int end)
        {
            string tail = text[end..].TrimStart();
            return tail.StartsWith("=", StringComparison.Ordinal) && !tail.StartsWith("==", StringComparison.Ordinal);
        }
    }

    private static void AssertNoConflictingGlobalVariableIds(UndertaleData data)
    {
        var conflicts = data.Variables
            .Where(v => v.InstanceType == UndertaleInstruction.InstanceType.Global)
            .GroupBy(v => v.Name.Content, StringComparer.Ordinal)
            .Select(group => (Name: group.Key, Ids: group.Select(v => v.VarID).Distinct().Order().ToArray()))
            .Where(x => x.Ids.Length > 1).ToArray();
        Assert.True(conflicts.Length == 0, "Composed profile has global variable names with multiple ids: "
            + string.Join(", ", conflicts.Select(x => x.Name + "=" + string.Join("/", x.Ids))));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "JAMM-CGF.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the JAMM-CGF repository root for ModKit example tests.");
    }
}

/// <summary>Enables this dedicated real archive tier only when its base path is supplied.</summary>
public sealed class RealFileBuiltinsFactAttribute : FactAttribute
{
    public RealFileBuiltinsFactAttribute()
    {
        string? path = Environment.GetEnvironmentVariable("JAMM_REAL_BUILTINS_BASE");
        if (string.IsNullOrWhiteSpace(path)) Skip = "Set JAMM_REAL_BUILTINS_BASE (or run tools/run-real-tests.sh) to enable this real-file tier.";
    }
}
