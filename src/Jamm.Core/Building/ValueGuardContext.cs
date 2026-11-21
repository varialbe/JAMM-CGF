using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

// Finds actual object inheritance, legacy/API catalog fish and the calling scope of GML scripts.
internal sealed class ValueGuardContext
{
    private readonly HashSet<string> fish = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> callers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UndertaleGameObject> objects;

    internal ValueGuardContext(UndertaleData data, IReadOnlyDictionary<string, GuardGml> code)
    {
        objects = data.GameObjects.ToDictionary(o => o.Name.Content, StringComparer.Ordinal);
        var scripts = data.Scripts.Where(s => s.Code is not null)
            .ToDictionary(s => s.Name.Content, s => s.Code.Name.Content, StringComparer.Ordinal);
        Dictionary<string, List<(string Script, string? Target)>> calls = new(StringComparer.Ordinal);
        foreach (var obj in data.GameObjects)
        {
            string name = obj.Name.Content;
            if (Inherits(name, "Fish")) fish.Add(name);
            foreach (var action in obj.Events.SelectMany(type => type).SelectMany(e => e.Actions))
                if (action.CodeId is { } entry) events.TryAdd(entry.Name.Content, name);
        }
        foreach (var (entry, gml) in code)
        {
            foreach (var call in gml.Calls())
            {
                string name = gml.Tokens[call.At].Value;
                if (AssetSymbols.Parse(name, out _, out string decoded)) name = decoded;
                if (name is "jamm_fish_register" or "jamm_catalog_register" && call.Arguments.Count > 1)
                    Register(gml, call.Arguments[1]);
                if (scripts.TryGetValue(name, out string? script))
                {
                    if (!calls.TryGetValue(entry, out var edges)) calls[entry] = edges = [];
                    string? target = gml.Resolve(call.At, null, "$caller");
                    edges.Add((script, target));
                }
            }
            if (entry == CatalogMerge.Create)
                foreach (var write in gml.Writes().Where(w => w.Field == "object" && w.Index is not null))
                    Register(gml, write.Value);
        }
        Queue<(string Entry, string Target)> pending = new(events.Select(e => (e.Key, e.Value)));
        foreach (var (entry, edges) in calls)
            foreach (var (script, target) in edges)
                if (target is not null and not "$caller") pending.Enqueue((script, target));
        while (pending.TryDequeue(out var item))
        {
            if (!callers.TryGetValue(item.Entry, out var known))
                callers[item.Entry] = known = new(StringComparer.Ordinal);
            if (!known.Add(item.Target) || !calls.TryGetValue(item.Entry, out var edges)) continue;
            foreach (var (script, target) in edges)
                if (target == "$caller") pending.Enqueue((script, item.Target));
        }

        void Register(GuardGml gml, GuardGml.Span span)
        {
            if (gml.ObjectName(span) is { } name && objects.ContainsKey(name)) fish.Add(name);
            else if (gml.Number(span) is double index && index >= 0 && index < data.GameObjects.Count && index == Math.Truncate(index))
                fish.Add(data.GameObjects[(int)index].Name.Content);
        }
    }

    internal string? EventOwner(string entry) => events.GetValueOrDefault(entry);
    internal IEnumerable<string> Owners(string entry) => callers.GetValueOrDefault(entry) ?? [];
    internal bool IsFish(string name) => fish.Contains(name);
    internal bool IsObject(string name) => objects.ContainsKey(name);
    internal IEnumerable<string> FishObjects => fish.Order(StringComparer.Ordinal);

    internal bool Inherits(string name, string parent)
    {
        var obj = objects.GetValueOrDefault(name);
        // A malformed cyclic parent chain must not stall the build.
        for (int i = 0; obj is not null && i < objects.Count; i++, obj = obj.ParentId)
            if (obj.Name.Content == parent) return true;
        return false;
    }
}
