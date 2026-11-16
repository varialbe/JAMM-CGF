using System.Security.Cryptography;
using System.Text;
using UndertaleModLib;

namespace Jamm.Core;

/// <summary>A live read-only input model. Treat Data as read-only and dispose this owner after use.</summary>
public sealed class LoadedGameFile : IDisposable
{
    /// <summary>The live UTML model, owned by this object; do not mutate it or dispose it separately.</summary>
    public UndertaleData Data { get; }
    /// <summary>Path-free metadata of the input file.</summary>
    public GameInspection Inspection { get; }
    /// <summary>Warnings reported while reading the file.</summary>
    public IReadOnlyList<string> Warnings { get; }

    internal LoadedGameFile(UndertaleData data, GameInspection inspection, IReadOnlyList<string> warnings)
        => (Data, Inspection, Warnings) = (data, inspection, warnings);

    /// <summary>Releases the live UTML model.</summary>
    public void Dispose() => Data.Dispose();
}

/// <summary>Loads game archives read-only without fingerprinting or console output.</summary>
public static class GameFileLoader
{
    /// <summary>Loads a live model. The caller must dispose the result; load errors are thrown.</summary>
    public static LoadedGameFile Load(string path, Action<string>? progress = null, CancellationToken cancellationToken = default, AnalysisOptions? options = null)
    {
        using var timing = options is null ? null : new AnalysisPhase(options, "load");
        path = Path.GetFullPath(path);
        List<string> warnings = [];
        using var stream = new CancellableGameStream(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke($"Loading {Path.GetFileName(path)} ({stream.Length:N0} bytes)");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        string storedVersion = ReadStoredVersion(stream);
        stream.Position = 0;
        var data = UndertaleIO.Read(stream, (warning, important) =>
            warnings.Add(FileMessages.WithoutPaths(important ? $"Important: {warning}" : warning, path)));
        try
        {
            var info = data.GeneralInfo ?? throw new InvalidDataException("The GEN8 game information chunk is missing.");
            string detected = $"{info.Major}.{info.Minor}.{info.Release}.{info.Build}";
            var counts = new Dictionary<AssetKind, int>
            {
                [AssetKind.Code] = data.Code?.Count ?? 0, [AssetKind.Scripts] = data.Scripts?.Count ?? 0,
                [AssetKind.Sprites] = data.Sprites?.Count ?? 0, [AssetKind.Objects] = data.GameObjects?.Count ?? 0,
                [AssetKind.Rooms] = data.Rooms?.Count ?? 0, [AssetKind.Sounds] = data.Sounds?.Count ?? 0,
                [AssetKind.Backgrounds] = data.Backgrounds?.Count ?? 0,
                [AssetKind.TexturePages] = data.EmbeddedTextures?.Count ?? 0,
                [AssetKind.TexturePageItems] = data.TexturePageItems?.Count ?? 0,
                [AssetKind.Strings] = data.Strings?.Count ?? 0, [AssetKind.Variables] = data.Variables?.Count ?? 0,
                [AssetKind.Functions] = data.Functions?.Count ?? 0, [AssetKind.Fonts] = data.Fonts?.Count ?? 0,
                [AssetKind.Shaders] = data.Shaders?.Count ?? 0, [AssetKind.Paths] = data.Paths?.Count ?? 0,
                [AssetKind.Timelines] = data.Timelines?.Count ?? 0, [AssetKind.Extensions] = data.Extensions?.Count ?? 0
            };
            var inspection = new GameInspection(Path.GetFileName(path), stream.Length, sha256, info.Name?.Content ?? "<unnamed>",
                info.DisplayName?.Content ?? "<unnamed>", storedVersion, detected, info.Branch.ToString(),
                info.BytecodeVersion, info.Timestamp, data.IsYYC() || data.Code is null || data.Code.Count == 0,
                !data.UnsupportedBytecodeVersion, counts);
            return new LoadedGameFile(data, inspection, warnings.ToArray());
        }
        catch
        {
            data.Dispose();
            throw;
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
