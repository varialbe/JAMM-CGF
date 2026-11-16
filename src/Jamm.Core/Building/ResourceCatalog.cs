using System.Collections;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal static class ResourceCatalog
{
    internal static Dictionary<string, IList> Tables(UndertaleData d) => new(StringComparer.Ordinal)
    {
        ["code"] = (IList)d.Code, ["scripts"] = (IList)d.Scripts, ["objects"] = (IList)d.GameObjects,
        ["rooms"] = (IList)d.Rooms, ["sprites"] = (IList)d.Sprites, ["sounds"] = (IList)d.Sounds,
        ["fonts"] = (IList)d.Fonts, ["backgrounds"] = (IList)d.Backgrounds, ["paths"] = (IList)d.Paths,
        ["timelines"] = (IList)d.Timelines
    };
    internal static string Name(object asset) => ((UndertaleNamedResource)asset).Name.Content;
    internal static object? Find(IList table, string name) => table.Cast<object>().SingleOrDefault(x => Name(x) == name);
}
