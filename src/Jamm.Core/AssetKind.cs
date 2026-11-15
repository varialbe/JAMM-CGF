namespace Jamm.Core;

/// <summary>GameMaker asset families used in inspection and comparison.</summary>
public enum AssetKind
{
    /// <summary>Bytecode entries.</summary>
    Code,
    /// <summary>Script definitions.</summary>
    Scripts,
    /// <summary>Sprite definitions and frames.</summary>
    Sprites,
    /// <summary>Object definitions.</summary>
    Objects,
    /// <summary>Rooms and their elements.</summary>
    Rooms,
    /// <summary>Sounds and embedded audio.</summary>
    Sounds,
    /// <summary>Font definitions and glyphs.</summary>
    Fonts,
    /// <summary>Backgrounds and tilesets.</summary>
    Backgrounds,
    /// <summary>Encoded texture atlases.</summary>
    TexturePages,
    /// <summary>Texture atlas regions (inspection only).</summary>
    TexturePageItems,
    /// <summary>String table values, represented by hashes.</summary>
    Strings,
    /// <summary>Variable definitions.</summary>
    Variables,
    /// <summary>Function definitions.</summary>
    Functions,
    /// <summary>Shaders (inspection only).</summary>
    Shaders,
    /// <summary>Paths (inspection only).</summary>
    Paths,
    /// <summary>Timelines (inspection only).</summary>
    Timelines,
    /// <summary>Extensions (inspection only).</summary>
    Extensions
}

/// <summary>Shared presentation and comparison rules for asset families.</summary>
public static class AssetKinds
{
    /// <summary>A readable label for reports.</summary>
    public static string Label(this AssetKind kind) => kind switch
    {
        AssetKind.TexturePages => "texture pages",
        AssetKind.TexturePageItems => "texture page items",
        _ => kind.ToString().ToLowerInvariant()
    };

    /// <summary>Whether the family requires VM bytecode.</summary>
    public static bool RequiresBytecode(this AssetKind kind) => kind is
        AssetKind.Code or AssetKind.Scripts or AssetKind.Variables or AssetKind.Functions;

    /// <summary>Whether the family contributes to the named-assets unchanged percentage.</summary>
    public static bool IsNamedAsset(this AssetKind kind) => kind is AssetKind.Code or AssetKind.Scripts
        or AssetKind.Sprites or AssetKind.Objects or AssetKind.Rooms or AssetKind.Sounds
        or AssetKind.Fonts or AssetKind.Backgrounds;
}
