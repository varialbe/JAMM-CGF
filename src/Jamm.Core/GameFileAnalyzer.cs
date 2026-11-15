using System.Security.Cryptography;
using System.Text;
using UndertaleModLib;

namespace Jamm.Core;

/// <summary>Loads input files read-only and releases UTML models after taking a compact snapshot.</summary>
public static class GameFileAnalyzer
{
    /// <summary>Inspects one file, capturing UTML warnings and isolating load/fingerprint failures.</summary>
    public static FileAnalysis Analyze(string path, bool includeAssets = true, Action<string>? progress = null)
    {
        List<string> warnings = [];
        GameInspection? inspection = null;
        try
        {
            path = Path.GetFullPath(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            progress?.Invoke($"Loading {path} ({stream.Length:N0} bytes)");
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
            stream.Position = 0;
            string storedVersion = ReadStoredVersion(stream);
            stream.Position = 0;
            using var data = UndertaleIO.Read(stream, (warning, important) =>
                warnings.Add(important ? $"Important: {warning}" : warning));
            var info = data.GeneralInfo ?? throw new InvalidDataException("The GEN8 game information chunk is missing.");
            string detected = $"{info.Major}.{info.Minor}.{info.Release}.{info.Build}";
            var counts = new Dictionary<string, int>
            {
                ["code"] = data.Code?.Count ?? 0, ["scripts"] = data.Scripts?.Count ?? 0,
                ["sprites"] = data.Sprites?.Count ?? 0, ["objects"] = data.GameObjects?.Count ?? 0,
                ["rooms"] = data.Rooms?.Count ?? 0, ["sounds"] = data.Sounds?.Count ?? 0,
                ["backgrounds"] = data.Backgrounds?.Count ?? 0,
                ["texture pages"] = data.EmbeddedTextures?.Count ?? 0,
                ["texture page items"] = data.TexturePageItems?.Count ?? 0,
                ["strings"] = data.Strings?.Count ?? 0, ["variables"] = data.Variables?.Count ?? 0,
                ["functions"] = data.Functions?.Count ?? 0, ["fonts"] = data.Fonts?.Count ?? 0,
                ["shaders"] = data.Shaders?.Count ?? 0, ["paths"] = data.Paths?.Count ?? 0,
                ["timelines"] = data.Timelines?.Count ?? 0, ["extensions"] = data.Extensions?.Count ?? 0
            };
            inspection = new(path, stream.Length, sha256, info.Name?.Content ?? "<unnamed>",
                info.DisplayName?.Content ?? "<unnamed>", storedVersion, detected, info.Branch.ToString(),
                info.BytecodeVersion, info.Timestamp, data.IsYYC() || data.Code is null || data.Code.Count == 0,
                !data.UnsupportedBytecodeVersion, counts);
            IReadOnlyDictionary<string, IReadOnlyList<AssetFingerprint>> assets =
                new Dictionary<string, IReadOnlyList<AssetFingerprint>>();
            if (includeAssets && inspection.BytecodeSupported)
            {
                progress?.Invoke($"Fingerprinting {Path.GetFileName(path)}");
                assets = new ContentFingerprints(warnings.Add).Snapshot(data);
            }
            return new(path, inspection, warnings, null, assets);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(path, inspection, warnings,
                $"could not read {path}: {ex.GetBaseException().Message.TrimEnd('.')}. Check that this is a complete, supported data.win file and that it is readable.",
                new Dictionary<string, IReadOnlyList<AssetFingerprint>>(), ex.ToString());
        }
    }

    // UTML replaces GEN8.Version with its format-detected version during loading. Preserve the
    // four on-disk uints at GEN8+44 too (see UTML's GeneralInfo.Serialize), without changing input.
    private static string ReadStoredVersion(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "FORM")
            throw new InvalidDataException("The file is not a GameMaker FORM archive.");
        uint length = reader.ReadUInt32();
        long end = 8L + length;
        if (end > stream.Length) throw new InvalidDataException("The file is truncated (FORM length exceeds file size).");
        while (stream.Position + 8 <= end)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint size = reader.ReadUInt32();
            long start = stream.Position;
            if (start + size > end) throw new InvalidDataException($"The {chunk} chunk extends beyond the file.");
            if (chunk == "GEN8")
            {
                if (size < 60) throw new InvalidDataException("The GEN8 chunk is truncated.");
                stream.Position = start + 44;
                return $"{reader.ReadUInt32()}.{reader.ReadUInt32()}.{reader.ReadUInt32()}.{reader.ReadUInt32()}";
            }
            stream.Position = start + size;
        }
        throw new InvalidDataException("The GEN8 game information chunk is missing.");
    }
}
